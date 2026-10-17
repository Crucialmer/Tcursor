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
                return false;

            dynamic d = acc;
            d.accLocation(out int left, out int top, out int width, out int height, 0);
            // 严格有效性校验：真实光标必须具有正的高度与宽度；
            // 当窗口无光标时，OBJID_CARET 的 accLocation 会返回 (0,0,0,0) 或假尺寸，必须判定为未命中；
            // 绝不能将 (0,0) 假坐标扩充为 (0,0,1,1)，否则会导致捕获链缓存受损并引发全局光标提示失效
            if (width <= 0 || height <= 0 || (left == 0 && top == 0 && width <= 1 && height <= 1))
                return false;

            caret = new CaretRect(left, top, left + width, top + height);
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
