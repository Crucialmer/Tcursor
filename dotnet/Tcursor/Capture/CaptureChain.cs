using Tcursor.Core;

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

    // 仅检测工作线程读写；Reset() 采用“换新实例”的方式避免跨线程并发访问
    private Dictionary<uint, string> _lastGood = new();
    private Dictionary<uint, Dictionary<string, Backoff>> _backoff = new();
    private readonly Dictionary<string, string[]> _parsedChains = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近一次成功使用的后端名（用于自检/诊断）</summary>
    public string? LastUsedMode { get; private set; }

    public bool TryCapture(uint pid, IntPtr hwnd, string chainSpec, out CaretRect rect)
    {
        rect = default;
        LastUsedMode = null;
        string[] names = ParseChain(chainSpec);
        if (names.Length == 0)
            return false;

        long now = Environment.TickCount64;
        Dictionary<string, Backoff> backoff = GetBackoffFor(pid);

        // 1) 最近成功的后端优先
        string? last = null;
        if (_lastGood.TryGetValue(pid, out last) && Contains(names, last))
        {
            if (!IsBackedOff(backoff, last, now) && TryRunTracked(backoff, last, hwnd, now, out rect))
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
            if (TryRunTracked(backoff, name, hwnd, now, out rect))
            {
                if (_lastGood.Count >= PerPidCacheMax)
                    _lastGood.Clear();
                _lastGood[pid] = name;
                LastUsedMode = name;
                return true;
            }
        }

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
            _ => null,
        };
        if (capture is null)
        {
            rect = default;
            return false;
        }
        return capture.TryGetCaret(hwnd, out rect);
    }

    /// <summary>执行单个后端：成功清空其退避记录；慢失败累计渐进退避；快失败不记（下周期可重试）。</summary>
    private bool TryRunTracked(Dictionary<string, Backoff> backoff, string name, IntPtr hwnd, long now, out CaretRect rect)
    {
        long t0 = Environment.TickCount64;
        bool ok = TryRun(name, hwnd, out rect);
        if (ok)
        {
            backoff.Remove(name);
            return true;
        }
        if (!name.Equals("GUI", StringComparison.OrdinalIgnoreCase) &&
            Environment.TickCount64 - t0 >= SlowFailureMs)
        {
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

    public void Dispose() => _uia.Dispose();
}
