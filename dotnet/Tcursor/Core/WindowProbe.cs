using System.Text;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Core;

/// <summary>窗口信息探测结果（对齐原版「窗口信息」五个字段）</summary>
internal readonly record struct WindowProbeResult(
    string Process, string RootClass, string FocusClass, string Title, string Path);

/// <summary>读取指定窗口的进程/类名/标题等信息（供「窗口信息」面板与自检使用）</summary>
internal static class WindowProbe
{
    public static WindowProbeResult GetInfo(IntPtr hwnd)
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
                try
                {
                    path = p.MainModule?.FileName ?? "";
                }
                catch
                {
                    path = ""; // 权限/位数受限时取不到
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
            var gti = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
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
