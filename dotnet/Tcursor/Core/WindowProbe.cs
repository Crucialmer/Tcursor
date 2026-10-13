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
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                process = p.ProcessName;
                if (includePath)
                {
                    try
                    {
                        path = p.MainModule?.FileName ?? "";
                    }
                    catch
                    {
                        path = ""; // 权限/位数受限时取不到
                    }
                }
            }
            catch
            {
                // 进程已退出等
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
}
