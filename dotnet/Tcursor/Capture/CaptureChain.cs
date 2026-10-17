using System.Runtime.InteropServices;
using Tcursor.Core;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Capture;

/// <summary>
/// 捕获链：按配置顺序依次尝试多种捕获后端。
/// 优化：
/// 1) 每进程记住最近成功的后端，下次优先尝试；
/// 2) 失败按“耗时”分类：≥ 慢失败阈值（超时/卡顿）记渐进退避（250ms→3s），
///    快速失败（目标无光标等常态，如浏览器焦点移出输入框）不记退避——下个周期直接重试（调用开销极小），
///    保证“焦点回来即恢复显示”；GUI 为轻量调用始终参与尝试；
/// 3) 退避仅在**实际尝试并失败**后累计/推进；跳过期间不刷新——修复“每次轮询重置退避、
///    后端一旦失败即被永久禁用”（浏览器光标丢失后不再恢复）的缺陷；
/// 4) 链解析结果缓存。
/// </summary>
internal sealed class CaptureChain : IDisposable
{
    private const long InitialBackoffMs = 250;
    private const long MaxBackoffMs = 3000;
    private const long SlowFailureMs = 100;   // 达到该耗时的失败视为超时/卡顿（记退避）；更快的失败不记
    private const int PerPidCacheMax = 256;   // per-PID 缓存上限（超限整体清空；与 ForegroundReader 同策略，防长时运行无界增长）

    private sealed class Backoff
    {
        public long Until;
        public int Strikes;
    }

    private readonly GuiCaretCapture _gui = new();
    private readonly UiaCaretCapture _uia = new();
    private readonly MsaaCaretCapture _msaa = new();
    private readonly TsfCaretCapture _tsf = new();

    // 仅检测工作线程读写；Reset() 采用“换新实例”的方式避免跨线程并发访问
    private Dictionary<uint, string> _lastGood = new();
    private Dictionary<uint, Dictionary<string, Backoff>> _backoff = new();
    private readonly Dictionary<string, string[]> _parsedChains = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近一次成功使用的后端名（用于自检/诊断）</summary>
    public string? LastUsedMode { get; private set; }

    public bool TryCapture(uint pid, IntPtr hwnd, string chainSpec, out CaretRect rect)
        => TryCapture(pid, hwnd, chainSpec, out rect, out _);

    /// <param name="transientFailure">失败是否属于瞬态（有后端超时/卡顿）：true 时调用方可在小宽限内保持显示防闪烁。</param>
    public bool TryCapture(uint pid, IntPtr hwnd, string chainSpec, out CaretRect rect, out bool transientFailure)
    {
        rect = default;
        transientFailure = false;
        LastUsedMode = null;
        string[] names = ParseChain(chainSpec);
        if (names.Length == 0)
            return false;

        long now = Environment.TickCount64;
        Dictionary<string, Backoff> backoff = GetBackoffFor(pid);
        bool slow = false;

        // 1) 最近成功的后端优先
        string? last = null;
        if (_lastGood.TryGetValue(pid, out last) && Contains(names, last))
        {
            if (!IsBackedOff(backoff, last, now) && TryRunTracked(backoff, last, hwnd, now, out rect, out slow))
            {
                LastUsedMode = last;
                return true;
            }
        }

        // 2) 按链顺序尝试其余后端
        foreach (string name in names)
        {
            if (last is not null && name.Equals(last, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsBackedOff(backoff, name, now))
                continue;
            if (TryRunTracked(backoff, name, hwnd, now, out rect, out bool slowThis))
            {
                if (_lastGood.Count >= PerPidCacheMax)
                    _lastGood.Clear();
                _lastGood[pid] = name;
                LastUsedMode = name;
                return true;
            }
            slow |= slowThis;
        }

        // 3) 智能兜底：当所有主要捕获后端全失败时（如管理员权限应用 UIPI 阻断 UIA，或自绘/WPF 独立输入框），
        // 尝试通过鼠标文本工字梁（IDC_IBEAM）或输入焦点控件边界推算光标
        if (TryFallbackCaret(hwnd, out rect))
        {
            LastUsedMode = "FALLBACK";
            return true;
        }

        transientFailure = slow;
        return false;
    }

    private static bool Contains(string[] names, string name)
    {
        foreach (string n in names)
        {
            if (n.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool TryRun(string name, IntPtr hwnd, out CaretRect rect)
    {
        // name 已由 ParseChain 统一大写（此处勿再 ToUpperInvariant：热路径上每次尝试都会多一次分配）
        ICaretCapture? capture = name switch
        {
            "GUI" => _gui,
            "UIA" => _uia,
            "MSAA" => _msaa,
            "TSF" => _tsf,
            _ => null,
        };
        if (capture is null)
        {
            rect = default;
            return false;
        }
        return capture.TryGetCaret(hwnd, out rect);
    }

    /// <summary>执行单个后端：成功清空其退避记录；慢失败累计渐进退避；快失败不记（下周期可重试）。
    /// <paramref name="slowFailure"/> 报告本次失败是否为慢失败（超时/卡顿）——供调用方区分瞬态与确定性失败。</summary>
    private bool TryRunTracked(Dictionary<string, Backoff> backoff, string name, IntPtr hwnd, long now, out CaretRect rect, out bool slowFailure)
    {
        long t0 = Environment.TickCount64;
        bool ok = TryRun(name, hwnd, out rect);
        slowFailure = false;
        if (ok)
        {
            backoff.Remove(name);
            return true;
        }
        if (!name.Equals("GUI", StringComparison.OrdinalIgnoreCase) &&
            Environment.TickCount64 - t0 >= SlowFailureMs)
        {
            slowFailure = true;
            Backoff b = backoff.TryGetValue(name, out Backoff? existing) ? existing : backoff[name] = new Backoff();
            b.Strikes++;
            int shift = Math.Min(b.Strikes - 1, 5);
            b.Until = now + Math.Min(MaxBackoffMs, InitialBackoffMs << shift);
        }
        return false;
    }

    private Dictionary<string, Backoff> GetBackoffFor(uint pid)
    {
        if (!_backoff.TryGetValue(pid, out var map))
        {
            if (_backoff.Count >= PerPidCacheMax)
                _backoff.Clear();
            _backoff[pid] = map = new Dictionary<string, Backoff>(StringComparer.OrdinalIgnoreCase);
        }
        return map;
    }

    private static bool IsBackedOff(Dictionary<string, Backoff> map, string name, long now)
        => map.TryGetValue(name, out Backoff? b) && b.Until > now;

    private string[] ParseChain(string spec)
    {
        string key = spec ?? "";
        if (_parsedChains.TryGetValue(key, out string[]? cached))
            return cached;
        string[] parts = key.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
            parts[i] = parts[i].ToUpperInvariant();
        _parsedChains[key] = parts;
        return parts;
    }

    /// <summary>清空缓存（配置变更时调用；换新实例，避免与工作线程并发访问同一字典）</summary>
    public void Reset()
    {
        _lastGood = new Dictionary<uint, string>();
        _backoff = new Dictionary<uint, Dictionary<string, Backoff>>();
    }

    /// <summary>
    /// 捕获全失败后的智能兜底：当目标应用（如跨进程 UIPI 阻断跨进程 UIA 的独立输入框）
    /// 存在明确的前台键盘焦点输入控件时，基于控件几何推算光标首行锚点。
    /// （注：绝不使用 IDC_IBEAM 鼠标指针位置兜底，防止鼠标移动到文本框内部即产生漂移误判）
    /// </summary>
    private static bool TryFallbackCaret(IntPtr hwnd, out CaretRect rect)
    {
        rect = default;
        try
        {
            // 检查前台 GUI 线程的键盘焦点窗口是否属于明确的文本输入类控件（如标准 Edit/RichEdit/文本框）
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(0, ref gti) && gti.hwndFocus != IntPtr.Zero)
            {
                var sb = new System.Text.StringBuilder(64);
                if (GetClassName(gti.hwndFocus, sb, sb.Capacity) > 0)
                {
                    string className = sb.ToString();
                    bool isEditClass = className.Equals("Edit", StringComparison.OrdinalIgnoreCase)
                        || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
                        || className.StartsWith("RICHEDIT", StringComparison.OrdinalIgnoreCase)
                        || className.StartsWith("MsftEdit", StringComparison.OrdinalIgnoreCase)
                        || className.IndexOf("TextBox", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isEditClass && GetWindowRect(gti.hwndFocus, out RECT focusRect))
                    {
                        int w = focusRect.Right - focusRect.Left;
                        int h = focusRect.Bottom - focusRect.Top;
                        if (w >= 16 && h >= 14)
                        {
                            int padX = Math.Min(8, Math.Max(4, w / 16));
                            int padY = Math.Min(4, Math.Max(2, (h - 20) / 2));
                            // 多行控件只取首行高度 20px，单行框（<=36px）贴合内部
                            int caretH = h <= 36 ? Math.Min(20, Math.Max(12, h - padY * 2)) : 20;
                            rect = new CaretRect(focusRect.Left + padX, focusRect.Top + padY, focusRect.Left + padX + 1, focusRect.Top + padY + caretH);
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
            // 兜底异常安全忽略
        }
        return false;
    }

    public void Dispose() => _uia.Dispose();
}
