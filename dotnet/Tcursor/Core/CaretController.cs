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

    // 窗口移动与拖拽平滑跟随（彻底消灭拖拽卡顿与拖影）
    private IntPtr _lastTargetHwnd;
    private RECT _lastWindowRect;
    private long _lastWindowMoveTick;

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

        // 监听系统会话切换与电源事件（锁屏解锁/睡眠恢复自动自愈）
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch { }
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
            bool isMoving = Environment.TickCount64 - _lastWindowMoveTick < 150;
            int waitMs = idle
                ? IdleWaitMs
                : isMoving ? 8 : (_shown ? Math.Max(5, config.PollIntervalMs) : HiddenWaitMs);

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

        WindowInfo win = _foreground.Read(_needsTitle, needClass: true);
        if (win.Hwnd == IntPtr.Zero || win.Pid == 0 || win.Pid == (uint)_ownPid || win.Process.Length == 0)
        {
            HideSymbol();
            _lastTargetHwnd = IntPtr.Zero;
            return;
        }

        RuntimeRule? rule = MatchRule(win);
        if (rule is null)
        {
            HideSymbol();
            _lastTargetHwnd = IntPtr.Zero;
            return;
        }

        // 窗口移动/拖动极速瞬时跟随（144Hz 极速跟手、零延迟、零拖影）：
        // 当用户按住标题栏拖动窗口或窗口正在平移动画时，输入光标在窗口内的相对坐标恒定不变。
        // 检测到窗口位移时，瞬时平移光标符号（直接在工作线程调用 UpdateLayeredWindow），
        // 且在拖动期间挂起耗时的跨进程 UIA/TSF 查询，彻底杜绝丢帧、卡顿与拖影！
        if (GetWindowRect(win.Hwnd, out RECT currentWinRect))
        {
            if (_lastTargetHwnd == win.Hwnd && _shown)
            {
                int dx = currentWinRect.Left - _lastWindowRect.Left;
                int dy = currentWinRect.Top - _lastWindowRect.Top;
                if (dx != 0 || dy != 0)
                {
                    int newX = _lastPos.X + dx;
                    int newY = _lastPos.Y + dy;
                    _lastPos = new Point(newX, newY);
                    _lastWindowRect = currentWinRect;
                    _lastWindowMoveTick = Environment.TickCount64;
                    _window.MoveTo(newX, newY);
                    return; // 瞬时跟随完成，跳过后续耗时 UIA 查询，保持 120 FPS 极速跟手
                }
            }
            _lastTargetHwnd = win.Hwnd;
            _lastWindowRect = currentWinRect;
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
            int cx = caret.Right > caret.Left + 1 ? caret.Right : caret.Left;
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

        // 光标行高智能校准与全局基准归一化：
        // Windows 各应用框架对光标高度的报告极不一致：
        // - Win32 Edit / EmEditor / 文件管理器：仅报告纯字形上升高度（14~16px），导致下方符号偏上；
        // - 微信 RichEdit / 自绘输入框 / TSF 选区：报告包含段落内边距甚至多行包围盒（30~120px），导致符号偏下；
        // - 现代标准文本行高（Notepad、Chrome、Word 默认等）：20~24px。
        // 对处于常规 UI 文本字号范围（<= 44px * scale）的光标，统一基准行高为 22px * scale，
        // 彻底消除不同软件在未输入/刚放入光标时提示符号高低不一、纵向漂移的问题。
        // 微信等即时通讯软件（WeChat/Weixin/QQ 等）输入框恒为单行文本基准行高，绝不被多行容器高度带偏。
        // 仅在非聊天应用且处于大字号排版（> 44px * scale，如大标题）时才尊重真实底部。
        int caretH = Math.Max(1, caret.Bottom - caret.Top);
        bool isChatApp = win.Process.IndexOf("WeChat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Process.IndexOf("Weixin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Process.IndexOf("WXWork", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Process.IndexOf("QQ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Process.IndexOf("TIM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Process.IndexOf("DingTalk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Process.IndexOf("Feishu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Process.IndexOf("Lark", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Class.IndexOf("WeChat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Class.IndexOf("Weixin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Class.IndexOf("ChatWnd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         win.Title.Contains("微信") ||
                         win.Title.IndexOf("WeChat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         rule.Name.Contains("微信") ||
                         rule.Name.IndexOf("WeChat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         rule.Name.IndexOf("Weixin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         rule.Name.IndexOf("WXWork", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (rule.Process?.ToString().IndexOf("WeChat", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                         (rule.Process?.ToString().IndexOf("Weixin", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                         (rule.Process?.ToString().IndexOf("WXWork", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

        int targetLineH = (int)Math.Round(22 * scale);
        int maxUiLineH = (int)Math.Round(44 * scale);

        // 微信/即时通讯类应用视觉对齐核心算法（彻底解决“搜索框偏上”与“偏下”）：
        // 1. TSF/Win32 下若 caretH 为 10~24px 范围，说明底层报告了真实的单行光标高度，有效底边直取 caret.Bottom；
        // 2. 若 caretH < 10px（TSF 选区退化残片/微小残线），底层 top 已处于文字行顶，必须补齐为微信标准 16px*scale 行高，
        //    彻底消除此前仅 +1px 导致搜索框在未输入/微小光标时突发严重偏上的缺陷；
        // 3. 若 caretH > 24px（多行编辑区或聊天框容器整框包围盒，如 48~150px），首行文字严格按 14px*scale 标准聊天字号折叠。
        // 通用窗口与现代编辑器（Notepad、Chrome、EmEditor、资源管理器等）维持标准 22px*scale 行高归一化。
        int effectiveBottom;
        if (isChatApp)
        {
            if (caretH >= (int)Math.Round(10 * scale) && caretH <= (int)Math.Round(24 * scale))
            {
                effectiveBottom = caret.Bottom;
            }
            else if (caretH < (int)Math.Round(10 * scale))
            {
                effectiveBottom = caret.Top + (int)Math.Round(16 * scale);
            }
            else
            {
                effectiveBottom = caret.Top + (int)Math.Round(14 * scale);
            }
        }
        else if (caretH <= maxUiLineH)
        {
            effectiveBottom = caret.Top + targetLineH;
        }
        else
        {
            effectiveBottom = caret.Bottom;
        }

        // 输拼音打字阶段与选区处理：若后端报告存在字符宽度的选区/组字串（Right > Left + 1），
        // 符号恒定锚定在组字串末尾（caret.Right），绝不在首尾来回跳跃
        int caretX = caret.Right > caret.Left + 1 ? caret.Right : caret.Left;
        int x = caretX + offsetX - pad;
        int y = (config.OriginBelow ? effectiveBottom : caret.Top) + offsetY - pad;

        bool stateChanged = state != _lastState;
        bool moved = x != _lastPos.X || y != _lastPos.Y;

        // 周期性核对符号窗口真实可见态（约每 2s）：外部最小化/“显示桌面”/句柄重建等会让内部标志
        // 与真实状态失联——发现失联即强制走重新显示路径
        if (_shown && Environment.TickCount64 - _lastVisibleCheckAt >= 2000)
        {
            _lastVisibleCheckAt = Environment.TickCount64;
            if (!_window.IsActuallyVisible())
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
        if (_shuttingDown || _window.IsDisposed)
        {
            _shown = false;
            return;
        }
        _shown = _window.ShowBitmap(bitmap, x, y);
    }

    private void HideSymbol()
    {
        if (!_shown)
            return;
        _shown = false;
        _lastTargetHwnd = IntPtr.Zero;
        if (!_shuttingDown && !_window.IsDisposed)
        {
            _window.HideSymbol();
        }
    }

    /// <summary>重置状态机与缓存（锁屏解锁/睡眠恢复/配置变更时自愈）</summary>
    public void ResetState()
    {
        _clearCacheRequested = true;
        _chain.Reset();
        _lastTargetHwnd = IntPtr.Zero;
        _lastPos = new Point(int.MinValue, int.MinValue);
        _lastState = null;
        _shown = false;
        _window.ResetGdi();
        Wake();
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        ResetState();
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        ResetState();
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
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch { }

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
