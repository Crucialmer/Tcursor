using System.Runtime.InteropServices;
using Tcursor.Core;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Capture;

/// <summary>
/// GUI 捕获：GetGUIThreadInfo 读取前台线程的光标矩形。
/// 最基础的捕获方式，开销极低，对大多数原生应用有效。
/// </summary>
internal sealed class GuiCaretCapture : ICaretCapture
{
    private static readonly int GtiSize = Marshal.SizeOf<GUITHREADINFO>();   // 常量：避免每次捕获运行时求尺寸

    public bool TryGetCaret(IntPtr hwnd, out CaretRect caret)
    {
        caret = default;
        try
        {
            uint tid = GetWindowThreadProcessId(hwnd, out _);
            if (tid == 0)
                return false;

            var gti = new GUITHREADINFO { cbSize = GtiSize };
            if (!GetGUIThreadInfo(tid, ref gti))
                return false;
            if (gti.hwndCaret == IntPtr.Zero)
                return false;

            var topLeft = new POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Top };
            var bottomRight = new POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Bottom };
            if (!ClientToScreen(gti.hwndCaret, ref topLeft) || !ClientToScreen(gti.hwndCaret, ref bottomRight))
                return false;

            // 坐标合理性校验（防止异常值把窗口丢到天外）
            if (Math.Abs(topLeft.X) > 200000 || Math.Abs(topLeft.Y) > 200000)
                return false;

            int diffX = bottomRight.X - topLeft.X;
            int diffY = bottomRight.Y - topLeft.Y;
            int caretW = Math.Max(1, Math.Abs(diffX));
            int caretH = Math.Max(14, Math.Abs(diffY));
            caret = new CaretRect(topLeft.X, topLeft.Y, topLeft.X + caretW, topLeft.Y + caretH);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
