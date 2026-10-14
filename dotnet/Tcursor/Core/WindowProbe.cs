using System.Text;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Core;

/// <summary>窗口信息探测结果（窗口信息面板的五个字段）</summary>
internal readonly record struct WindowProbeResult(
    string Process, string RootClass, string FocusClass, string Title, string Path);

/// <summary>读取指定窗口的进程/类名/标题等信息（供「窗口信息」面板与自检使用）</summary>
internal static class WindowProbe
{
    private static readonly int GtiSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>();   // 常量：避免每次调用运行时求尺寸

    // pid → 进程名 缓存（进程名不随进程变化；「窗口信息」面板 120ms 轮询时不必反复跨进程查询）
    private const long NameCacheTtlMs = 30000;
    private const int NameCacheMax = 256;
    private static readonly object NameGate = new();
    private static readonly Dictionary<uint, (string Name, long At)> NameCache = new();

    /// <param name="includePath">是否查询进程路径（MainModule 为跨进程模块查询，是最贵的一项；调用方可按 pid 缓存后跳过）</param>
    public static WindowProbeResult GetInfo(IntPtr hwnd, bool includePath = true)
    {
        if (hwnd == IntPtr.Zero)
            return new WindowProbeResult("", "", "", "", "");

        uint tid = GetWindowThreadProcessId(hwnd, out uint pid);

        string process = "";
        string path = "";
        if (pid != 0)
        {
            process = ProcessNameCached(pid);
            if (includePath)
            {
                try
                {
                    using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                    path = p.MainModule?.FileName ?? "";
                }
                catch
                {
                    path = ""; // 权限/位数受限时取不到
                }
            }
        }

        var buffer = new StringBuilder(512);

        buffer.Clear();
        GetClassName(hwnd, buffer, buffer.Capacity);
        string rootClass = buffer.ToString();

        string focusClass = "";
        if (tid != 0)
        {
            var gti = new GUITHREADINFO { cbSize = GtiSize };
            if (GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != IntPtr.Zero)
            {
                buffer.Clear();
                GetClassName(gti.hwndFocus, buffer, buffer.Capacity);
                focusClass = buffer.ToString();
            }
        }

        buffer.Clear();
        GetWindowText(hwnd, buffer, buffer.Capacity);
        string title = buffer.ToString();

        return new WindowProbeResult(process, rootClass, focusClass, title, path);
    }

    /// <summary>pid → 进程名（30s 缓存，避免高频轮询下的跨进程查询；上限满时整表清空，与捕获链缓存同策略）</summary>
    private static string ProcessNameCached(uint pid)
    {
        lock (NameGate)
        {
            if (NameCache.TryGetValue(pid, out var entry) && Environment.TickCount64 - entry.At < NameCacheTtlMs)
                return entry.Name;
        }
        string name = "";
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch
        {
            // 进程已退出等：缓存空名，由 TTL 到期后自然重试
        }
        lock (NameGate)
        {
            if (NameCache.Count >= NameCacheMax)
                NameCache.Clear();
            NameCache[pid] = (name, Environment.TickCount64);
        }
        return name;
    }
}
