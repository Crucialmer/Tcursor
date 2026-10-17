using System.Text;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Core;

/// <summary>
/// 前台窗口信息读取器：复用缓冲区 + 进程名短时缓存，按需读取标题/类名（降低每帧开销）。
/// 仅在检测工作线程使用（无并发访问）。
/// </summary>
internal sealed class ForegroundReader
{
    private const int ProcNameCacheTtlMs = 30_000;
    private const int ProcNameCacheMax = 256;

    private static readonly WindowInfo Empty = new(IntPtr.Zero, 0, "", "", "");

    private readonly StringBuilder _classBuffer = new(256);
    private readonly StringBuilder _titleBuffer = new(512);
    private readonly Dictionary<uint, (string Name, long Ticks)> _processNames = new();

    private static readonly int GtiSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>();

    /// <summary>读取前台窗口信息；needTitle/needClass 为 false 时跳过对应系统调用与字符串分配</summary>
    public WindowInfo Read(bool needTitle = true, bool needClass = true)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return Empty;

        var gti = new GUITHREADINFO { cbSize = GtiSize };
        if (GetGUIThreadInfo(0, ref gti))
        {
            if (gti.hwndActive != IntPtr.Zero && gti.hwndActive != hwnd)
            {
                // 如果当前前台线程内有激活的弹出窗口（如 Quicker 快捷搜索框、独立悬浮输入面板），优先采纳该活动窗口
                hwnd = gti.hwndActive;
            }
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return Empty;

        string title = "";
        if (needTitle)
        {
            _titleBuffer.Clear();
            GetWindowText(hwnd, _titleBuffer, _titleBuffer.Capacity);
            title = _titleBuffer.ToString();
        }

        string cls = "";
        if (needClass)
        {
            _classBuffer.Clear();
            GetClassName(hwnd, _classBuffer, _classBuffer.Capacity);
            cls = _classBuffer.ToString();
        }

        return new WindowInfo(hwnd, pid, GetProcessName(pid), title, cls);
    }

    private string GetProcessName(uint pid)
    {
        long now = Environment.TickCount64;
        if (_processNames.TryGetValue(pid, out var entry) && now - entry.Ticks < ProcNameCacheTtlMs)
            return entry.Name;

        string name = "";
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch
        {
            // 进程已退出或无权访问：缓存空名（短时）
        }

        if (_processNames.Count >= ProcNameCacheMax)
            _processNames.Clear();
        _processNames[pid] = (name, now);
        return name;
    }
}
