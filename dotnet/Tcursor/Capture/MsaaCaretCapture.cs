using System.Runtime.InteropServices;
using Tcursor.Core;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Capture;

/// <summary>
/// MSAA 捕获：oleacc AccessibleObjectFromWindow + OBJID_CARET。
/// 覆盖部分 GUI/UIA 都取不到光标的旧式应用。
/// </summary>
internal sealed class MsaaCaretCapture : ICaretCapture
{
    private static readonly Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    private const uint OBJID_CARET = 0xFFFFFFF8;

    public bool TryGetCaret(IntPtr hwnd, out CaretRect caret)
    {
        caret = default;
        object? acc = null;
        try
        {
            Guid iid = IID_IAccessible;
            int hr = AccessibleObjectFromWindow(hwnd, OBJID_CARET, ref iid, out acc);
            if (hr != 0 || acc is null)
            {
                // 尝试从当前前台线程的键盘焦点子控件提取（针对 Chrome 等顶层窗口包含子渲染/视图宿主）
                var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
                if (GetGUIThreadInfo(0, ref gti) && gti.hwndFocus != IntPtr.Zero && gti.hwndFocus != hwnd)
                {
                    hr = AccessibleObjectFromWindow(gti.hwndFocus, OBJID_CARET, ref iid, out acc);
                }
            }
            if (hr != 0 || acc is null)
                return false;

            dynamic d = acc;
            d.accLocation(out int left, out int top, out int width, out int height, 0);
            // 严格有效性校验：真实光标必须具有正的高度；宽度可为 0（如垂直单线光标）或正数；
            // 当窗口无光标时，OBJID_CARET 的 accLocation 会返回 (0,0,0,0) 或假尺寸，必须判定为未命中
            if (width < 0 || height <= 0 || (left == 0 && top == 0 && width <= 1 && height <= 1))
                return false;

            int w = width <= 0 ? 1 : width;
            caret = new CaretRect(left, top, left + w, top + height);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (acc is not null)
            {
                try { Marshal.ReleaseComObject(acc); } catch { }
            }
        }
    }
}
