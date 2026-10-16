using System.Drawing;
using System.Drawing.Drawing2D;

namespace Tcursor.Ui;

/// <summary>
/// macOS 风格窗口基类：去掉系统标题栏，自绘顶部标题区（红绿灯 + 居中标题，随主题深浅色）。
/// 交互：按住标题区拖动窗口；红灯（悬停显示 ×）关闭；Esc 取消 / Alt+F4 关闭沿用系统惯例。
/// </summary>
internal class MacDialogForm : Form
{
    /// <summary>标题区高度（96 DPI 设计基线像素，macOS 26 舒适黄金比例）</summary>
    public const int HeaderDesignPx = 42;

    /// <summary>把窗口摆到工作区右下角（托盘区附近）——托盘小工具的「提示 / 信息」类窗口统一落点。
    /// 需在窗口最终尺寸确定后调用（OnLoad 及以后）。</summary>
    public static void PositionBottomRight(Form form, int designMargin = 24)
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        int margin = UiTheme.Dpi(form, designMargin);
        form.Location = new Point(
            Math.Max(area.Left, area.Right - form.Width - margin),
            Math.Max(area.Top, area.Bottom - form.Height - margin));
    }

    private bool _trafficHover;
    private bool _redDown;

    public MacDialogForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        UpdateHeaderPadding();
    }

    public float HeaderHeight => UiTheme.Dpi(this, HeaderDesignPx);

    private void UpdateHeaderPadding()
    {
        int hh = (int)HeaderHeight;
        if (Padding.Top != hh)
            Padding = new Padding(0, hh, 0, 0);
    }

    private RectangleF RedDotRect()
    {
        float s = UiTheme.DpiScale(this);
        float d = 12f * s;
        return new RectangleF(16f * s, HeaderHeight / 2f - d / 2f, d, d);
    }

    private RectangleF TrafficGroupRect()
    {
        float s = UiTheme.DpiScale(this);
        float d = 12f * s;
        float gap = 8f * s;
        float totalW = d * 3f + gap * 2f;
        return new RectangleF(16f * s - 4f * s, HeaderHeight / 2f - d / 2f - 4f * s, totalW + 8f * s, d + 8f * s);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateHeaderPadding();
        ApplyDwmAttributes(UiTheme.IsDark);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateHeaderPadding();
        Invalidate();
    }

    /// <summary>更新窗口 DWM 属性（圆角 + 亚克力/云母材质 + 沉浸深浅模式同步）</summary>
    internal void UpdateDwmTheme(bool dark)
    {
        if (IsHandleCreated)
            ApplyDwmAttributes(dark);
    }

    private void ApplyDwmAttributes(bool dark)
    {
        try
        {
            int pref = Win32.NativeMethods.DWMWCP_ROUND;   // Win11 圆角
            _ = Win32.NativeMethods.DwmSetWindowAttribute(Handle, Win32.NativeMethods.DWMWA_WINDOW_CORNER_RADIUS, ref pref, sizeof(int));

            int darkVal = dark ? 1 : 0;
            _ = Win32.NativeMethods.DwmSetWindowAttribute(Handle, Win32.NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

            // Win11 亚克力半透明磨砂材质
            int backdrop = Win32.NativeMethods.DWMSBT_TRANSIENTWINDOW;
            _ = Win32.NativeMethods.DwmSetWindowAttribute(Handle, Win32.NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
        catch (Exception ex)
        {
            Core.Log.Error(ex);
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW：无边框窗口阴影
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        UiPalette p = UiTheme.Current;
        float s = UiTheme.DpiScale(this);
        int hh = (int)HeaderHeight;
        bool active = Form.ActiveForm == this;

        // 窗口四周 1px 细微边界（解决深色模式下与纯黑背景贴合无边的问题）
        using (var borderPen = new Pen(p.CardBorder, 1f))
            g.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

        // 标题区底部分隔线
        using (var pen = new Pen(p.HeaderLine, Math.Max(1f, 1f * s)))
            g.DrawLine(pen, 0, hh - 1, ClientSize.Width, hh - 1);

        // 红绿灯（原生 macOS 连续曲率圆 + 0.5px 精细外框环 + 悬停微符文）
        float d = 12f * s;
        float gap = 8f * s;
        float x = 16f * s;
        float cy = hh / 2f;

        bool showGlyphs = _trafficHover && active;
        // 红灯 (关闭)
        DrawTrafficLight(g, new RectangleF(x, cy - d / 2f, d, d),
            active ? p.TrafficRed : p.TrafficInactive,
            active ? p.TrafficRedBorder : p.TrafficInactive,
            showGlyphs ? "×" : null);

        x += d + gap;
        // 黄灯 (最小化/不可用)
        DrawTrafficLight(g, new RectangleF(x, cy - d / 2f, d, d),
            active ? DimTraffic(p.TrafficYellow, p.WindowBg) : p.TrafficInactive,
            active ? DimTraffic(p.TrafficYellowBorder, p.WindowBg) : p.TrafficInactive,
            showGlyphs ? "−" : null);

        x += d + gap;
        // 绿灯 (缩放/全屏)
        DrawTrafficLight(g, new RectangleF(x, cy - d / 2f, d, d),
            active ? DimTraffic(p.TrafficGreen, p.WindowBg) : p.TrafficInactive,
            active ? DimTraffic(p.TrafficGreenBorder, p.WindowBg) : p.TrafficInactive,
            showGlyphs ? "+" : null);

        // 居中标题（优雅层次，字重与主题匹配）
        TextRenderer.DrawText(g, Text, UiTheme.FontTitle, new Rectangle(0, 0, ClientSize.Width, hh),
            active ? p.HeaderTitle : p.HeaderTitleInactive,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private void DrawTrafficLight(Graphics g, RectangleF rect, Color fillColor, Color borderColor, string? glyph)
    {
        float s = UiTheme.DpiScale(this);
        using (var brush = new SolidBrush(fillColor))
            g.FillEllipse(brush, rect);

        using (var pen = new Pen(borderColor, Math.Max(0.5f, 0.6f * s)))
            g.DrawEllipse(pen, rect);

        if (string.IsNullOrEmpty(glyph))
            return;

        using var glyphBrush = new SolidBrush(UiTheme.Current.TrafficGlyph);
        using var glyphFont = new Font(Font.FontFamily, 7.5f * s, FontStyle.Bold);
        var r = new Rectangle((int)rect.X, (int)rect.Y - (int)(1f * s), (int)rect.Width, (int)rect.Height);
        TextRenderer.DrawText(g, glyph, glyphFont, r, UiTheme.Current.TrafficGlyph,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static Color DimTraffic(Color color, Color surface)
        => Color.FromArgb(
            (color.A + surface.A) / 2,
            (color.R * 65 + surface.R * 35) / 100,
            (color.G * 65 + surface.G * 35) / 100,
            (color.B * 65 + surface.B * 35) / 100);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && e.Y < HeaderHeight)
        {
            if (RedDotRect().Contains(e.Location))
            {
                _redDown = true;
                Capture = true;   // 捕获鼠标：保证在窗体外的释放也能收到 MouseUp
                return;
            }
            // 拖拽标题区移动窗口（等效系统标题栏拖拽）
            Win32.NativeMethods.ReleaseCapture();
            Win32.NativeMethods.SendMessage(Handle, Win32.NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)Win32.NativeMethods.HTCAPTION, IntPtr.Zero);
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_redDown)
        {
            _redDown = false;
            Capture = false;
            if (RedDotRect().Contains(e.Location))
                Close();
            else
                InvalidateHeader();
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool hover = TrafficGroupRect().Contains(e.Location);
        if (hover != _trafficHover)
        {
            _trafficHover = hover;
            InvalidateHeader();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_trafficHover)
        {
            _trafficHover = false;
            InvalidateHeader();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture && _redDown)
        {
            _redDown = false;   // 捕获意外丢失（如 Alt+Tab）：复位按下态
            InvalidateHeader();
        }
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        InvalidateHeader();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        InvalidateHeader();
    }

    private void InvalidateHeader()
        => Invalidate(new Rectangle(0, 0, ClientSize.Width, (int)HeaderHeight));
}
