using System.Drawing;
using Tcursor.Capture;
using Tcursor.Config;
using Tcursor.Render;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Core;

/// <summary>
/// 核心控制器：工作线程检测循环（前台窗口 → 白名单规则 → 状态检测 → 捕获链 → 符号显示）。
///
/// v0.2.2 优化：按需读取前台信息、无规则快速短路、exe 名/DPI 缓存；
/// v0.2.1 修复：空闲隐藏顺序、隐藏后重显、退出边界；v0.2 优化点：
/// - 自适应轮询：显示时快频（可配，默认 20ms）/ 隐藏时降频 120ms / 空闲暂停（前台事件与配置变更即时唤醒）
/// - 低分配检测循环：前台信息复用缓冲、进程名短时缓存、状态键查表（无每帧字符串构造）
/// - 规则正则预编译（RuntimeRule）；样式对象引用比对触发重绘（无字符串签名）
/// - 捕获链每进程缓存 + 失败退避；UIA 调用在独立线程限时等待（见 UiaCaretCapture）
/// </summary>
internal sealed class CaretController : IDisposable
{
    private const int HiddenWaitMs = 120;
    private const int IdleWaitMs = 200;
    private const long CaptureGraceMs = 300;   // 捕获瞬时失败宽限：期内保持显示（防渲染抖动/超时闪烁）

    private static readonly string[] StateKeys = Enum.GetNames<InputState>();

    private readonly object _configGate = new();
    private readonly CaptureChain _chain = new();
    private readonly ForegroundReader _foreground = new();
    private readonly LayeredSymbolWindow _window;
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly Thread _worker;
    private readonly int _ownPid = Environment.ProcessId;

    private AppConfig _config;
    private volatile RuntimeRule[] _rules = Array.Empty<RuntimeRule>();
    private volatile bool _needsTitle;
    private volatile bool _needsClass;
    private volatile bool _quit;
    private volatile bool _shuttingDown;
    private volatile bool _paused;

    // 检测线程独占的辅助缓存
    private readonly Dictionary<string, string> _exeNames = new();
    private IntPtr _dpiMonitor;
    private double _dpiScale;
    private long _dpiCheckedAt;

    // 状态机（仅工作线程访问）
    private Point _lastPos = new(int.MinValue, int.MinValue);
    private InputState? _lastState;
    private double _lastScale;
    private bool _shown;
    private long _lastCaptureOkAt;
    private long _lastVisibleCheckAt;

    // 状态位图预渲染池（每种状态 + 当前 DPI 缩放只渲染一次并缓存，中英/大小写切换时零 GC 分配零延迟）
    private readonly Dictionary<InputState, (SymbolStyle Style, Bitmap Bitmap)> _stateBitmaps = new();
    private int _cachedCornerPreference = -1;
    private int _cachedEdgeStyle = -1;
    private volatile bool _clearCacheRequested;

    public CaretController(AppConfig config, LayeredSymbolWindow window)
    {
        _config = config;
        _window = window;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "Tcursor.Worker",
        };
        RebuildRules(config);
    }

    public void Start() => _worker.Start();

    /// <summary>唤醒工作线程立即执行一次检测（前台窗口变化/配置变更时调用）</summary>
    public void Wake()
    {
        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // 退出中：忽略
        }
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        Wake();
    }

    public void UpdateConfig(AppConfig config)
    {
        lock (_configGate)
        {
            _config = config;
            RebuildRules(config);
        }
        _clearCacheRequested = true;
        _chain.Reset();
        Wake();
    }

    private void RebuildRules(AppConfig config)
    {
        var list = new List<RuntimeRule>(config.Rules.Count);
        bool needsTitle = false;
        bool needsClass = false;
        foreach (RuleConfig rule in config.Rules)
        {
            if (!rule.Enabled)
                continue;
            RuntimeRule? compiled = RuntimeRule.TryCompile(rule);
            if (compiled is null)
                continue;
            list.Add(compiled);
            needsTitle |= compiled.Title is not null;
            needsClass |= compiled.Class is not null;
        }
        _rules = list.ToArray();
        _needsTitle = needsTitle;
        _needsClass = needsClass;
    }

    private void WorkerLoop()
    {
        while (!_quit)
        {
            AppConfig config;
            lock (_configGate)
                config = _config;

            uint idleMs = SysInfo.GetIdleMs();   // 每轮只取一次（Tick 复用，避免同值双算）
            bool idle = idleMs > (uint)Math.Max(0, config.IdleStopMs);
            int waitMs = idle
                ? IdleWaitMs
                : _shown ? Math.Max(5, config.PollIntervalMs) : HiddenWaitMs;

            bool signaled;
            try
            {
                signaled = _wake.Wait(waitMs);
                if (signaled)
                    _wake.Reset();
            }
            catch (ObjectDisposedException)
            {
                break; // 退出中：直接结束
            }
            if (_quit)
                break;

            try
            {
                Tick(config, idleMs);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }

    private void Tick(AppConfig config, uint idleMs)
    {
        if (!config.Enabled || _paused)
        {
            HideSymbol();
            return;
        }

        if (config.HideDelayMs > 0 && idleMs > (uint)Math.Max(0, config.HideDelayMs))
        {
            HideSymbol(); // 空闲隐藏（先于暂停判断，否则 HideDelay >= IdleStop 时永不生效）
            return;
        }

        if (idleMs > (uint)Math.Max(0, config.IdleStopMs))
            return; // 输入空闲：暂停检测（保持当前显示）

        if (_rules.Length == 0)
        {
            HideSymbol(); // 无生效规则（强制白名单）：不可能显示，直接短路
            return;
        }

        WindowInfo win = _foreground.Read(_needsTitle, _needsClass);
        if (win.Hwnd == IntPtr.Zero || win.Pid == 0 || win.Pid == (uint)_ownPid || win.Process.Length == 0)
        {
            HideSymbol();
            return;
        }

        RuntimeRule? rule = MatchRule(win);
        if (rule is null)
        {
            HideSymbol();
            return;
        }

        string chainSpec = rule.CaptureChain.Length > 0 ? rule.CaptureChain : config.DefaultCaptureChain;
        // TSF 最后手段：其余后端全失败时，为封闭应用（微信 4.x 等）追加进程内读取（见 TsfCaretCapture）
        if (config.TsfFallback && chainSpec.Length > 0 && chainSpec.IndexOf("TSF", StringComparison.OrdinalIgnoreCase) < 0)
            chainSpec += ">TSF";

        if (!_chain.TryCapture(win.Pid, win.Hwnd, chainSpec, out CaretRect caret, out bool transientFailure))
        {
            // 捕获失败分两类（v2.0.2）：
            //  · 瞬态失败（后端超时/目标繁忙而失败，transientFailure=true）：小宽限内保持显示，避免渲染抖动闪烁；
            //  · 确定性失败（各后端都很快地回答「此处无光标」——例如焦点已离开输入框/点了非输入区域）：
            //    立即隐藏。此前一律宽限 300ms，表现为「离开输入框后符号还残留一会儿」。
            if (_shown && transientFailure && Environment.TickCount64 - _lastCaptureOkAt <= CaptureGraceMs)
                return;
            HideSymbol();
            return;
        }
        _lastCaptureOkAt = Environment.TickCount64;

        // 光标应在窗口范围内（滚动出视野时不显示）
        if (GetWindowRect(win.Hwnd, out RECT windowRect))
        {
            int cx = caret.Left;
            int cy = (caret.Top + caret.Bottom) / 2;
            if (cx < windowRect.Left - 24 || cx > windowRect.Right + 24 ||
                cy < windowRect.Top - 24 || cy > windowRect.Bottom + 24)
            {
                HideSymbol();
                return;
            }
        }

        // 状态检测放在「捕获成功 + 光标在窗口内」之后：中文布局下它要向 IME 窗口发跨进程消息（SendMessageTimeout），
        // 而状态只在确认要显示时才被消费——未命中/无光标的 tick 不必付这笔开销（行为不变，采样点最多后移捕获耗时）
        InputState state = StateDetector.Detect(win.Hwnd);
        string stateKey = StateKeys[(int)state];
        if (!config.Styles.TryGetValue(stateKey, out SymbolStyle? style) || style.Kind == SymbolKind.None)
        {
            HideSymbol();
            return;
        }

        double scale = DpiScaleFor(caret.Left, caret.Top);

        // 检查配置变更或 DPI 缩放变更：清理已缓存的状态位图
        if (_clearCacheRequested ||
            Math.Abs(scale - _lastScale) > 0.001 ||
            config.ShapeCornerPreference != _cachedCornerPreference ||
            config.ShapeEdgeStyle != _cachedEdgeStyle)
        {
            ClearBitmapCache();
            _lastScale = scale;
            _cachedCornerPreference = config.ShapeCornerPreference;
            _cachedEdgeStyle = config.ShapeEdgeStyle;
            _clearCacheRequested = false;
        }

        // 获取或预渲染当前状态位图（命中缓存则零分配、零延迟直接复用）
        Bitmap? currentBitmap = GetOrRenderBitmap(state, style, config, scale);
        if (currentBitmap is null)
        {
            HideSymbol();
            return;
        }

        // 偏移 = 全局偏移（所有状态共用）+ 规则追加偏移（v0.6.3 起样式级偏移已废弃）
        int offsetX = (int)Math.Round((rule.OffsetX + config.ScreenOffsetX) * scale);
        int offsetY = (int)Math.Round((rule.OffsetY + config.ScreenOffsetY) * scale);
        // 「柔和阴影」边框样式会在位图四周扩出阴影边距：显示位置相应回补，保证可见形状锚点不变
        int pad = SymbolRenderer.GetShadowPadding(style, config.ShapeEdgeStyle, scale);
        int x = caret.Left + offsetX - pad;
        int y = (config.OriginBelow ? caret.Bottom : caret.Top) + offsetY - pad;

        bool stateChanged = state != _lastState;
        bool moved = x != _lastPos.X || y != _lastPos.Y;

        // 周期性核对符号窗口真实可见态（约每 2s）：外部最小化/“显示桌面”/句柄重建等会让内部标志
        // 与真实状态失联——此前表现为“跑久了所有应用都不再显示、需重启”，发现失联即强制走重新显示路径
        if (_shown && Environment.TickCount64 - _lastVisibleCheckAt >= 2000)
        {
            _lastVisibleCheckAt = Environment.TickCount64;
            if (!InvokeOnUi(() => _window.IsActuallyVisible()))
                _shown = false;
        }

        // 注意：即使状态与位置都未变，只要当前是“隐藏”状态（曾 HideSymbol），也要重新显示
        if (!stateChanged && !moved && _shown)
            return;

        ShowAt(currentBitmap, x, y);
        _lastState = state;
        _lastPos = new Point(x, y);
    }

    private Bitmap? GetOrRenderBitmap(InputState state, SymbolStyle style, AppConfig config, double scale)
    {
        if (_stateBitmaps.TryGetValue(state, out var entry))
        {
            if (ReferenceEquals(entry.Style, style))
                return entry.Bitmap;
            // 样式实例被替换：释放旧位图重新生成
            entry.Bitmap.Dispose();
            _stateBitmaps.Remove(state);
        }

        Bitmap? fresh = SymbolRenderer.Render(style, config.ShapeCornerPreference, config.ShapeEdgeStyle, scale);
        if (fresh is not null)
            _stateBitmaps[state] = (style, fresh);
        return fresh;
    }

    private void ClearBitmapCache()
    {
        foreach (var entry in _stateBitmaps.Values)
            entry.Bitmap.Dispose();
        _stateBitmaps.Clear();
        _lastState = null;
    }

    // ---------------- 显示/隐藏 ----------------

    private void ShowAt(Bitmap bitmap, int x, int y)
    {
        _shown = InvokeOnUi(() => _window.ShowBitmap(bitmap, x, y));
    }

    private void HideSymbol()
    {
        if (!_shown)
            return;
        _shown = false;
        InvokeOnUi(() =>
        {
            _window.HideSymbol();
            return true;
        });
    }

    /// <summary>在 UI 线程执行；返回动作结果（窗口已销毁/调用失败返回 false）</summary>
    private bool InvokeOnUi(Func<bool> action)
    {
        if (_shuttingDown || _window.IsDisposed || !_window.IsHandleCreated)
            return false;
        try
        {
            return _window.InvokeRequired ? (bool)_window.Invoke(action)! : action();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return false;
        }
    }

    // ---------------- 规则匹配 ----------------

    private RuntimeRule? MatchRule(in WindowInfo win)
    {
        string processExe = GetProcessExeName(win.Process);
        foreach (RuntimeRule rule in _rules)
        {
            if (rule.Matches(win.Process, processExe, win.Title, win.Class))
                return rule;
        }
        return null;
    }

    /// <summary>进程名 → “进程名.exe” 缓存（检测线程独占；限量防膨胀）</summary>
    private string GetProcessExeName(string process)
    {
        if (_exeNames.TryGetValue(process, out string? cached))
            return cached;
        if (_exeNames.Count >= 64)
            _exeNames.Clear();
        string exe = process + ".exe";
        _exeNames[process] = exe;
        return exe;
    }

    // ---------------- DPI ----------------

    /// <summary>按显示器缓存 DPI 缩放（同一显示器 5 秒内不重复查询）</summary>
    private double DpiScaleFor(int x, int y)
    {
        try
        {
            IntPtr monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return _dpiScale > 0 ? _dpiScale : 1.0;

            long now = Environment.TickCount64;
            if (monitor == _dpiMonitor && _dpiScale > 0 && now - _dpiCheckedAt < 5000)
                return _dpiScale;

            if (GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                _dpiMonitor = monitor;
                _dpiScale = dpiX / 96.0;
                _dpiCheckedAt = now;
                return _dpiScale;
            }
        }
        catch
        {
            // 取不到按上次值/100%
        }
        return _dpiScale > 0 ? _dpiScale : 1.0;
    }

    public void Dispose()
    {
        // 退出顺序：先置「正在关闭」——工作线程此后不再向界面线程投递（否则界面线程已离开消息循环时，
        // 工作线程会阻塞在 Invoke 上直到 Join 超时，退出必然多等一整个超时）
        _shuttingDown = true;
        _quit = true;
        Wake();
        try
        {
            _worker.Join(500);   // 有标志兜底后，工作线程最多还在一次 tick 里（捕获后端各自有限时）
        }
        catch
        {
            // 忽略等待失败
        }
        ClearBitmapCache();
        _chain.Dispose();
        _wake.Dispose();
    }
}
