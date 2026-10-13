using System.Drawing;
using System.Runtime.InteropServices;
using Tcursor.Ui;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Render;

/// <summary>
/// 符号窗口：分层窗口（逐像素透明）+ 点击穿透 + 永不抢焦点 + 不显示在 Alt+Tab。
///
/// v0.2.2 优化：缓存屏幕 DC / 内存 DC / 位图副本（HBITMAP）——
/// 同位图、同尺寸的“仅移动”更新走快速路径，只调用 UpdateLayeredWindow，不再重建 GDI 对象。
/// </summary>
internal sealed class LayeredSymbolWindow : Form
{
    private bool _visible;

    // GDI 缓存（仅 UI 线程访问）
    private IntPtr _screenDc = IntPtr.Zero;
    private IntPtr _memDc = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _oldBitmap = IntPtr.Zero;
    private Bitmap? _cachedBitmap;
    private int _cachedWidth;
    private int _cachedHeight;

    public LayeredSymbolWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1, 1);
        Text = "Tcursor.Symbol";
        // 不需要键盘/焦点交互
        SetStyle(ControlStyles.Selectable, false);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000008  // WS_EX_TOPMOST
                        | 0x00000080  // WS_EX_TOOLWINDOW（不显示在 Alt+Tab）
                        | 0x08000000  // WS_EX_NOACTIVATE（不抢焦点）
                        | 0x00080000  // WS_EX_LAYERED（分层，逐像素透明）
                        | 0x00000020; // WS_EX_TRANSPARENT（鼠标点击穿透）
            return cp;
        }
    }

    /// <summary>在屏幕物理坐标 (x, y) 显示位图（保留逐像素透明）。
    /// 返回是否更新成功——失败（显示器/桌面拓扑变化后缓存的 DC 失效等）会丢弃 GDI 缓存，下次调用整体重建重试。</summary>
    internal bool ShowBitmap(Bitmap bitmap, int x, int y)
    {
        if (_screenDc == IntPtr.Zero)
        {
            _screenDc = GetDC(IntPtr.Zero);
            if (_screenDc == IntPtr.Zero)
                return false;
        }

        // 位图变化（或尺寸变化）才重建 GDI 缓存；仅移动时复用（快速路径）
        if (!ReferenceEquals(_cachedBitmap, bitmap) || _cachedWidth != bitmap.Width || _cachedHeight != bitmap.Height)
        {
            if (!RebuildBitmapCache(bitmap))
                return false;
        }

        var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
        var sourcePos = new POINT { X = 0, Y = 0 };
        var destPos = new POINT { X = x, Y = y };
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA,
        };

        if (!UpdateLayeredWindow(Handle, _screenDc, ref destPos, ref size, _memDc, ref sourcePos, 0, ref blend, ULW_ALPHA))
        {
            // 返回值必须检查：失败时位图不会显示，若仍报告成功，控制器会一直走「仅移动」快速路径而不重试
            ReleaseBitmapCache();
            ReleaseScreenDc();
            return false;
        }

        if (!_visible)
        {
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            _visible = true;
        }
        return true;
    }

    private void ReleaseScreenDc()
    {
        if (_screenDc != IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, _screenDc);
            _screenDc = IntPtr.Zero;
        }
    }

    internal void HideSymbol()
    {
        if (!_visible)
            return;
        ShowWindow(Handle, SW_HIDE);
        _visible = false;
    }

    /// <summary>按当前位图重建 GDI 缓存（内存 DC + 位图副本）</summary>
    private bool RebuildBitmapCache(Bitmap bitmap)
    {
        ReleaseBitmapCache();

        _memDc = CreateCompatibleDC(_screenDc);
        if (_memDc == IntPtr.Zero)
            return false;

        _hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        if (_hBitmap == IntPtr.Zero)
        {
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
            return false;
        }

        _oldBitmap = SelectObject(_memDc, _hBitmap);
        _cachedBitmap = bitmap;
        _cachedWidth = bitmap.Width;
        _cachedHeight = bitmap.Height;
        return true;
    }

    private void ReleaseBitmapCache()
    {
        if (_oldBitmap != IntPtr.Zero && _memDc != IntPtr.Zero)
        {
            SelectObject(_memDc, _oldBitmap);
            _oldBitmap = IntPtr.Zero;
        }
        if (_hBitmap != IntPtr.Zero)
        {
            DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }
        if (_memDc != IntPtr.Zero)
        {
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        _cachedBitmap = null;
        _cachedWidth = 0;
        _cachedHeight = 0;
    }

    protected override void WndProc(ref Message m)
    {
        // 跟随系统深浅色：WM_SETTINGCHANGE("ImmersiveColorSet") 时重新检测主题
        if (m.Msg == 0x001A && m.LParam != IntPtr.Zero &&
            string.Equals(Marshal.PtrToStringUni(m.LParam), "ImmersiveColorSet", StringComparison.Ordinal))
        {
            UiTheme.DetectAndApply();
        }
        // 显示器拓扑变化（外接/投影/RDP/分辨率切换）：缓存的屏幕 DC 与位图可能失效，提前丢弃由下次调用重建
        if (m.Msg == 0x007E)   // WM_DISPLAYCHANGE
        {
            ReleaseBitmapCache();
            ReleaseScreenDc();
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseBitmapCache();
        ReleaseScreenDc();
        base.Dispose(disposing);
    }
}
