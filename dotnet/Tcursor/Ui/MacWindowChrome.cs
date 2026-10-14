using System.Drawing;
using System.Drawing.Drawing2D;

namespace Tcursor.Ui;

/// <summary>
/// macOS 风格窗口基类：去掉系统标题栏，自绘顶部标题区（红绿灯 + 居中标题，随主题深浅色）。
/// 交互：按住标题区拖动窗口；红灯（悬停显示 ×）关闭；Esc 取消 / Alt+F4 关闭沿用系统惯例。
/// </summary>
internal class MacDialogForm : Form
{
    /// <summary>标题区高度（96 DPI 设计基线像素）</summary>
    public const int HeaderDesignPx = 36;

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

    private bool _redHover;
    private bool _redDown;

    public MacDialogForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Padding = new Padding(0, HeaderDesignPx, 0, 0);   // 标题区占位（随 DPI 由框架缩放）
    }

    private float HeaderHeight => UiTheme.Dpi(this, HeaderDesignPx);

    private RectangleF RedDotRect()
    {
        float s = UiTheme.DpiScale(this);
        float d = 12f * s;
        return new RectangleF(16f * s, HeaderHeight / 2f - d / 2f, d, d);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int pref = Win32.NativeMethods.DWMWCP_ROUND;   // Win11 圆角（低版本调用失败即忽略）
            _ = Win32.NativeMethods.DwmSetWindowAttribute(Handle, Win32.NativeMethods.DWMWA_WINDOW_CORNER_RADIUS, ref pref, sizeof(int));
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
            cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW：无边框窗口投影
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
        bool active = ActiveForm == this;

        // 标题区底部分隔线（macOS 统一标题栏风格）
        using (var pen = new Pen(p.HeaderLine, Math.Max(1f, s)))
            g.DrawLine(pen, 0, hh - 1, ClientSize.Width, hh - 1);

        // 红绿灯（红=关闭；黄/绿当前为固定尺寸对话框，呈禁用态颜色）
        float d = 12f * s;
        float gap = 8f * s;
        float x = 16f * s;
        float cy = hh / 2f;
        DrawTrafficLight(g, new RectangleF(x, cy - d / 2f, d, d), active ? p.TrafficRed : p.TrafficInactive, active && _redHover);
        x += d + gap;
        DrawTrafficLight(g, new RectangleF(x, cy - d / 2f, d, d), active ? p.TrafficYellow : p.TrafficInactive, false);
        x += d + gap;
        DrawTrafficLight(g, new RectangleF(x, cy - d / 2f, d, d), active ? p.TrafficGreen : p.TrafficInactive, false);

        // 居中标题
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, ClientSize.Width, hh),
            active ? p.HeaderTitle : p.HeaderTitleInactive,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private void DrawTrafficLight(Graphics g, RectangleF rect, Color color, bool showGlyph)
    {
        using (var brush = new SolidBrush(color))
            g.FillEllipse(brush, rect);
        if (!showGlyph)
            return;
        using var pen = new Pen(UiTheme.Current.TrafficGlyph, Math.Max(1f, 1.2f * UiTheme.DpiScale(this)));
        float m = rect.Width * 0.32f;
        g.DrawLine(pen, rect.X + m, rect.Y + m, rect.Right - m, rect.Bottom - m);
        g.DrawLine(pen, rect.Right - m, rect.Y + m, rect.X + m, rect.Bottom - m);
    }

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
        bool hover = RedDotRect().Contains(e.Location);
        if (hover != _redHover)
        {
            _redHover = hover;
            InvalidateHeader();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_redHover)
        {
            _redHover = false;
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
