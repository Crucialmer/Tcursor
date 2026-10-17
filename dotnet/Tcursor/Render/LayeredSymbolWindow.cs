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
    private readonly object _renderGate = new();

    // GDI 缓存（受 _renderGate 保护，支持工作线程零开销直调）
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

        // 系统会话切换（锁屏 Win+L、解锁、用户切换）与电源恢复（休眠唤醒）：
        // 自动重置 GDI 屏幕 DC 与位图缓存，杜绝长时运行后 DC 失效导致符号消失
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch
        {
        }
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
    /// 支持工作线程直接调用（无消息泵跨线程延迟），受 _renderGate 线程同步保护。</summary>
    internal bool ShowBitmap(Bitmap bitmap, int x, int y)
    {
        lock (_renderGate)
        {
            if (_screenDc == IntPtr.Zero)
            {
                _screenDc = GetDC(IntPtr.Zero);
                if (_screenDc == IntPtr.Zero)
                    return false;
            }

            // 先核对真实可见态并恢复（含从最小化恢复），再写入内容与位置——顺序不可颠倒
            if (!_visible || IsIconic(Handle) || !IsWindowVisible(Handle))
            {
                ShowWindow(Handle, SW_SHOWNOACTIVATE);
                _visible = true;
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
                ReleaseBitmapCache();
                ReleaseScreenDc();
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 极速位移：用于目标窗口拖拽/移动期间以零开销（&lt;0.05ms、零 GDI 重建、零内存分配）瞬间平移符号窗口，
    /// 彻底消除拖动窗口时的拖影卡顿感与延迟。
    /// </summary>
    internal bool MoveTo(int x, int y)
    {
        lock (_renderGate)
        {
            if (!_visible || _screenDc == IntPtr.Zero || _memDc == IntPtr.Zero || _cachedBitmap == null)
                return false;

            var size = new SIZE { cx = _cachedWidth, cy = _cachedHeight };
            var sourcePos = new POINT { X = 0, Y = 0 };
            var destPos = new POINT { X = x, Y = y };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA,
            };

            return UpdateLayeredWindow(Handle, _screenDc, ref destPos, ref size, _memDc, ref sourcePos, 0, ref blend, ULW_ALPHA);
        }
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
        lock (_renderGate)
        {
            if (!_visible)
                return;
            ShowWindow(Handle, SW_HIDE);
            _visible = false;
        }
    }

    /// <summary>真实可见态（OS 查询，非内部标志）：供检测循环做周期性失联校验。</summary>
    internal bool IsActuallyVisible()
    {
        lock (_renderGate)
            return _visible && IsWindowVisible(Handle) && !IsIconic(Handle);
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

    /// <summary>强制重置所有 GDI 缓存（供系统拓扑变化、锁屏、睡眠唤醒后自愈）</summary>
    internal void ResetGdi()
    {
        lock (_renderGate)
        {
            ReleaseBitmapCache();
            ReleaseScreenDc();
            _visible = false;
        }
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        ResetGdi();
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        ResetGdi();
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
            ResetGdi();
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch
        {
        }
        ResetGdi();
        base.Dispose(disposing);
    }
}
