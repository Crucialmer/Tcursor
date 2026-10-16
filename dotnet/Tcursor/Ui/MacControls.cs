using System.Drawing.Drawing2D;
using System.Globalization;

namespace Tcursor.Ui;

/// <summary>macOS 风格控件的公共绘制工具</summary>
internal static class MacDraw
{
    /// <summary>连续曲率圆角矩形路径（Apple/Figma 算法，与符号圆角同一几何）</summary>
    public static GraphicsPath RoundedPath(RectangleF rect, float radius)
        => Render.SymbolRenderer.SmoothRectPath(rect.X, rect.Y, rect.Width, rect.Height, radius, 0.6f);

    /// <summary>键盘焦点环（macOS 观感）：围绕给定形状画一圈强调色，与形状之间留出窄间隙。
    /// 仅在 ShowFocusCues（用户正用键盘导航）时调用——鼠标点击聚焦不显示，与 macOS 一致。</summary>
    public static void DrawFocusRing(Graphics g, RectangleF shape, float radius, float scale, Color? color = null)
    {
        float gap = Math.Max(1f, 1f * scale);
        float w = Math.Max(1.5f, 1.8f * scale);
        var outer = RectangleF.Inflate(shape, gap + w / 2f, gap + w / 2f);
        using var path = RoundedPath(outer, radius + gap + w / 2f);
        using var pen = new Pen(color ?? UiTheme.Accent, w);
        g.DrawPath(pen, path);
    }

    public static void FillSurface(Graphics g, Control c)
    {
        using var brush = new SolidBrush(c.BackColor);
        g.FillRectangle(brush, c.ClientRectangle);
    }

    /// <summary>绘制 macOS 风格复选框（圆角方框 + 白色对勾）。盒宽即尺寸，内部几何按比例缩放；
    /// surface = 未勾选时方框内的填充面（与所在表面一致，避免深色下出现亮块）。</summary>
    public static void DrawCheckBox(Graphics g, RectangleF box, bool isChecked, bool enabled, Color surface)
    {
        float u = box.Width / 18f;   // 以 18 设计像素方盒为基准的单位
        using var path = RoundedPath(box, 5f * u);
        if (isChecked)
        {
            using var brush = new SolidBrush(enabled ? UiTheme.Accent : UiTheme.Dim);
            g.FillPath(brush, path);
            using var pen = new Pen(Color.White, Math.Max(1.4f, 2.2f * u))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            var pts = new[]
            {
                new PointF(box.X + 4.6f * u, box.Y + 9.6f * u),
                new PointF(box.X + 7.6f * u, box.Y + 12.6f * u),
                new PointF(box.X + 13.6f * u, box.Y + 5.6f * u),
            };
            g.DrawLines(pen, pts);
        }
        else
        {
            using var brush = new SolidBrush(surface);
            g.FillPath(brush, path);
            using var pen = new Pen(UiTheme.ControlBorder, Math.Max(1f, 1.2f * u));
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>macOS 风格按钮：圆角、悬停/按下反馈、强调色主按钮（随主题深浅色）</summary>
internal sealed class MacButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Accent { get; set; }
    private bool _hover;
    private bool _down;
    private Color? _swatch;

    /// <summary>颜色示例模式：填充为指定颜色（用于取色按钮），文本颜色自动取对比色</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color? Swatch
    {
        get => _swatch;
        set { _swatch = value; Invalidate(); }
    }

    public MacButton(bool accent = false)
    {
        Accent = accent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Height = 32;
        BackColor = UiTheme.WindowBg;
        Font = UiTheme.Font;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        MacDraw.FillSurface(g, this);

        float penW = Math.Max(1f, UiTheme.DpiF(this, 1f));
        var rect = new RectangleF(penW / 2f, penW / 2f, Width - penW, Height - penW);
        Color fill;
        Color borderColor = UiTheme.ControlBorder;
        if (!Enabled)
            fill = Accent ? UiTheme.AccentDisabled : UiTheme.ButtonDisabledFace;
        else if (Accent)
            fill = _down ? UiTheme.AccentDown : _hover ? UiTheme.AccentHover : UiTheme.Accent;
        else
            fill = _down ? UiTheme.ButtonDown : _hover ? UiTheme.ButtonHover : UiTheme.ButtonFace;

        using (var path = MacDraw.RoundedPath(rect, UiTheme.DpiF(this, 8f)))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
            if (!Accent)
            {
                using var pen = new Pen(borderColor, penW);
                g.DrawPath(pen, path);
            }
        }

        // 取色器模式：左侧精致颜色徽标圆点，右侧十六进制文本（随系统深浅色自适应）
        if (_swatch.HasValue)
        {
            float s = UiTheme.DpiScale(this);
            float dotD = Math.Max(12f, 15f * s);
            float dotX = rect.X + 8f * s;
            float dotY = rect.Y + (rect.Height - dotD) / 2f;
            var dotRect = new RectangleF(dotX, dotY, dotD, dotD);
            using (var dotPath = new GraphicsPath())
            {
                dotPath.AddEllipse(dotRect);
                using var dotBrush = new SolidBrush(_swatch.Value);
                g.FillPath(dotBrush, dotPath);
                using var dotPen = new Pen(Color.FromArgb(60, 0, 0, 0), Math.Max(1f, 1f * s));
                g.DrawPath(dotPen, dotPath);
            }

            int textLeft = (int)Math.Round(dotX + dotD + 7f * s);
            var textRect = new Rectangle(textLeft, (int)rect.Y, (int)(rect.Width - (textLeft - rect.X) - 6 * s), (int)rect.Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? UiTheme.Text : UiTheme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        else
        {
            Color textColor = !Enabled
                ? UiTheme.Dim
                : Accent
                    ? Color.White
                    : UiTheme.Text;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        // 键盘焦点环（内描边；只在键盘导航时出现）。主按钮上取白色，其余取强调色。
        if (Focused && ShowFocusCues)
        {
            float ringW = Math.Max(1.5f, UiTheme.DpiF(this, 1.8f));
            var inner = RectangleF.Inflate(rect, -(penW + ringW), -(penW + ringW));
            if (inner.Width > 2 && inner.Height > 2)
            {
                using var ringPath = MacDraw.RoundedPath(inner, Math.Max(2f, UiTheme.DpiF(this, 8f) - ringW));
                using var ringPen = new Pen(Accent ? Color.FromArgb(235, 255, 255, 255) : UiTheme.Accent, ringW);
                g.DrawPath(ringPen, ringPath);
            }
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _down = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

/// <summary>macOS 风格单选：圆点 + 文本（随主题深浅色）</summary>
internal sealed class MacRadio : RadioButton
{
    public MacRadio()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        BackColor = UiTheme.WindowBg;
        Font = UiTheme.Font;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size t = TextRenderer.MeasureText(Text, Font);
        return new Size(UiTheme.Dpi(this, 28) + t.Width, Math.Max(UiTheme.Dpi(this, 24), t.Height + UiTheme.Dpi(this, 6)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        MacDraw.FillSurface(g, this);

        float s = UiTheme.DpiScale(this);
        int cy = Height / 2;
        var circle = new RectangleF(3f * s, cy - 9f * s, 18f * s, 18f * s);   // x=3s：给键盘焦点环留出边距
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(circle);
            if (Checked)
            {
                using var brush = new SolidBrush(Enabled ? UiTheme.Accent : UiTheme.Dim);
                g.FillPath(brush, path);
                using var dot = new SolidBrush(Color.White);
                g.FillEllipse(dot, circle.X + 5.5f * s, circle.Y + 5.5f * s, 7f * s, 7f * s);
            }
            else
            {
                using var brush = new SolidBrush(BackColor);   // 未选中面 = 所在表面色（窗口/卡片），随主题
                g.FillPath(brush, path);
                using var pen = new Pen(UiTheme.ControlBorder, Math.Max(1f, 1.2f * s));
                g.DrawPath(pen, path);
            }
        }

        if (Focused && ShowFocusCues)
        {
            using var ringPen = new Pen(UiTheme.Accent, Math.Max(1.5f, 1.8f * s));
            g.DrawEllipse(ringPen, RectangleF.Inflate(circle, 2f * s, 2f * s));
        }

        int textX = (int)Math.Round(25f * s);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textX, 0, Width - textX, Height),
            Enabled ? UiTheme.Text : UiTheme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

/// <summary>macOS 风格复选框：圆角方框 + 白色对勾（随主题深浅色）</summary>
internal sealed class MacCheck : CheckBox
{
    public MacCheck()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        BackColor = UiTheme.WindowBg;
        Font = UiTheme.Font;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size t = TextRenderer.MeasureText(Text, Font);
        return new Size(UiTheme.Dpi(this, 28) + t.Width, Math.Max(UiTheme.Dpi(this, 24), t.Height + UiTheme.Dpi(this, 6)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        MacDraw.FillSurface(g, this);

        float s = UiTheme.DpiScale(this);
        int cy = Height / 2;
        var box = new RectangleF(3f * s, cy - 9f * s, 18f * s, 18f * s);   // x=3s：给键盘焦点环留出边距
        MacDraw.DrawCheckBox(g, box, Checked, Enabled, BackColor);   // 与规则列表的自绘复选框共用同一几何
        if (Focused && ShowFocusCues)
            MacDraw.DrawFocusRing(g, box, 5f * s, s);

        int textX = (int)Math.Round(25f * s);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textX, 0, Width - textX, Height),
            Enabled ? UiTheme.Text : UiTheme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

/// <summary>macOS 风格开关（药丸 + 圆钮，随主题深浅色；旋钮带 120ms 缓动）</summary>
internal sealed class MacSwitch : CheckBox
{
    private const int TrackW = 42;   // 设计像素（96 DPI 基线）
    private const int TrackH = 24;
    private const int AnimDurationMs = 120;

    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private float _knobT;              // 0=关 1=开（动画中为插值）
    private float _animFrom;
    private float _animTo;
    private long _animAt;

    private int TrackWpx => UiTheme.Dpi(this, TrackW);
    private int TrackHpx => UiTheme.Dpi(this, TrackH);

    public MacSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        BackColor = UiTheme.WindowBg;
        Font = UiTheme.Font;
        _anim.Tick += (_, _) => AnimTick();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size t = TextRenderer.MeasureText(Text, Font);
        return new Size(TrackWpx + (t.Width > 0 ? UiTheme.Dpi(this, 8) + t.Width : 0) + UiTheme.Dpi(this, 4),
                        Math.Max(TrackHpx + UiTheme.Dpi(this, 4), t.Height + UiTheme.Dpi(this, 6)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        MacDraw.FillSurface(g, this);

        float s = UiTheme.DpiScale(this);
        int cy = Height / 2;
        int trackW = TrackWpx;
        int trackH = TrackHpx;
        var track = new RectangleF(1.5f * s, cy - trackH / 2f, trackW, trackH);
        using (var path = new GraphicsPath())
        {
            path.FillMode = FillMode.Winding;   // 三段子路径叠加：Winding 填充避免 even-odd 空洞
            path.AddEllipse(track);
            path.AddEllipse(track.X + trackW - trackH, track.Y, trackH, trackH);
            path.AddRectangle(new RectangleF(track.X + trackH / 2f, track.Y, trackW - trackH, trackH));
            using var brush = new SolidBrush(Checked ? (Enabled ? UiTheme.Accent : UiTheme.Dim) : UiTheme.TrackBg);
            g.FillPath(brush, path);
        }

        float knobD = trackH - 4f * s;
        float knobX = track.X + 2f * s + _knobT * (trackW - knobD - 4f * s);   // 两端 = 关/开位置，动画中取插值
        using (var knob = new GraphicsPath())
        {
            knob.AddEllipse(knobX, track.Y + 2f * s, knobD, knobD);
            using var brush = new SolidBrush(Color.White);
            g.FillPath(brush, knob);
            using var pen = new Pen(Color.FromArgb(28, 0, 0, 0), Math.Max(1f, s));
            g.DrawPath(pen, knob);
        }

        if (Focused && ShowFocusCues)
            MacDraw.DrawFocusRing(g, track, trackH / 2f, s);

        if (!string.IsNullOrEmpty(Text))
        {
            int textX = trackW + (int)Math.Round(9f * s);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(textX, 0, Width - textX, Height),
                Enabled ? UiTheme.Text : UiTheme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        StartAnim(Checked ? 1f : 0f);
        Invalidate();
        base.OnCheckedChanged(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    /// <summary>旋钮滑动动画（120ms ease-out）。程序化赋值（表单载入值）或系统关闭界面动效时直接落位，
    /// 避免打开设置窗时一排开关集体“自演”。</summary>
    private void StartAnim(float target)
    {
        bool animate = IsHandleCreated && Visible && SystemInformation.UIEffectsEnabled;
        _anim.Stop();
        _animFrom = _knobT;
        _animTo = target;
        if (!animate || Math.Abs(_knobT - target) < 0.01f)
        {
            _knobT = target;
            return;
        }
        _animAt = Environment.TickCount64;
        _anim.Start();
    }

    private void AnimTick()
    {
        float t = Math.Clamp((Environment.TickCount64 - _animAt) / (float)AnimDurationMs, 0f, 1f);
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);   // ease-out cubic
        _knobT = _animFrom + (_animTo - _animFrom) * eased;
        if (t >= 1f)
        {
            _knobT = _animTo;
            _anim.Stop();
        }
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _anim.Stop();
            _anim.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>macOS 风格滑杆（细轨 + 圆形滑块，支持拖动/点击；随主题深浅色）</summary>
internal sealed class MacSlider : Control
{
    private int _value;
    private bool _dragging;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Minimum { get; set; } = 0;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum { get; set; } = 255;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value)
                return;
            _value = v;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;

    public MacSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 24;
        BackColor = UiTheme.WindowBg;
        Cursor = Cursors.Hand;
    }

    private const float KnobDesignD = 16f;   // 设计像素（96 DPI 基线）

    private float KnobD => UiTheme.DpiF(this, KnobDesignD);

    private float KnobX()
    {
        float t = (Maximum - Minimum) <= 0 ? 0f : (_value - (float)Minimum) / (Maximum - Minimum);
        return KnobD / 2f + t * (Width - KnobD);
    }

    private void SetFromMouseX(int x)
    {
        float usable = Math.Max(1f, Width - KnobD);
        float t = Math.Clamp((x - KnobD / 2f) / usable, 0f, 1f);
        Value = (int)Math.Round(Minimum + t * (Maximum - Minimum));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        MacDraw.FillSurface(g, this);

        float s = UiTheme.DpiScale(this);
        float knobD = KnobD;
        float cy = Height / 2f;
        float left = knobD / 2f;
        float right = Width - knobD / 2f;
        float knobX = KnobX();
        float halfTrack = 2.5f * s;

        using (var track = MacDraw.RoundedPath(new RectangleF(left, cy - halfTrack, Math.Max(1f, right - left), halfTrack * 2f), halfTrack))
        using (var trackBrush = new SolidBrush(UiTheme.TrackBg))
        {
            g.FillPath(trackBrush, track);
        }

        float fillW = Math.Max(0f, knobX - left);
        if (fillW > 0.5f)
        {
            using var fill = MacDraw.RoundedPath(new RectangleF(left, cy - halfTrack, fillW, halfTrack * 2f), halfTrack);
            using var fillBrush = new SolidBrush(Enabled ? UiTheme.Accent : UiTheme.Dim);
            g.FillPath(fillBrush, fill);
        }

        using (var knob = new GraphicsPath())
        {
            knob.AddEllipse(knobX - knobD / 2f, cy - knobD / 2f, knobD, knobD);
            using var brush = new SolidBrush(Color.White);
            g.FillPath(brush, knob);
            using var pen = new Pen(Color.FromArgb(40, 0, 0, 0), Math.Max(1f, s));
            g.DrawPath(pen, knob);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
        {
            _dragging = true;
            Capture = true;
            SetFromMouseX(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && Enabled)
            SetFromMouseX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture && _dragging)
            _dragging = false;   // 捕获意外丢失（如 Alt+Tab 切走）：复位拖动
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;   // 禁用态显示默认箭头（与 macOS 一致）
        if (!Enabled)
        {
            _dragging = false;
            Capture = false;
        }
        Invalidate();
        base.OnEnabledChanged(e);
    }
}

/// <summary>macOS 风格卡片容器（圆角白底 + 细边框 + 可选标题；随主题深浅色，完美适配 8K 缩放）</summary>
internal sealed class MacCard : Panel
{
    public string? Title { get; }

    public MacCard(string? title = null)
    {
        Title = title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.WindowBg;
        Font = UiTheme.Font;
        Padding = Title is null
            ? new Padding(16)
            : new Padding(16, 38, 16, 12);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int innerW = proposedSize.Width > 0 ? Math.Max(0, proposedSize.Width - Padding.Horizontal) : 0;
        int maxH = 0;
        int maxW = 0;
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            Size s = c.GetPreferredSize(new Size(innerW, 0));
            maxH = Math.Max(maxH, s.Height);
            maxW = Math.Max(maxW, s.Width);
        }
        int minH = MinimumSize.Height;
        int minW = MinimumSize.Width;
        return new Size(Math.Max(Math.Max(maxW + Padding.Horizontal, 200), minW),
                        Math.Max(maxH + Padding.Vertical, minH));
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        MacDraw.FillSurface(g, this);

        float penW = Math.Max(1f, UiTheme.DpiF(this, 1f));
        var rect = new RectangleF(penW / 2f, penW / 2f, Width - penW, Height - penW);
        using (var path = MacDraw.RoundedPath(rect, UiTheme.DpiF(this, 12f)))
        {
            using var fill = new SolidBrush(UiTheme.CardBg);
            g.FillPath(fill, path);
            using var pen = new Pen(UiTheme.CardBorder, penW);
            g.DrawPath(pen, path);
        }

        if (!string.IsNullOrEmpty(Title))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var brush = new SolidBrush(UiTheme.SectionTitle);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString(Title, UiTheme.FontSectionTitle, brush,
                new PointF(UiTheme.Dpi(this, 16), UiTheme.Dpi(this, 12)), sf);
        }
    }
}

/// <summary>macOS 26 风格药丸分段选择器（极光胶囊切换卡，随主题自适应与 8K 矢量渲染）</summary>
internal sealed class MacSegmentedControl : Control
{
    private string[] _items;
    private int _selectedIndex;
    private int _hoverIndex = -1;

    public event EventHandler? SelectedIndexChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value >= 0 && value < _items.Length && value != _selectedIndex)
            {
                _selectedIndex = value;
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string[] Items
    {
        get => _items;
        set
        {
            _items = value ?? Array.Empty<string>();
            _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _items.Length - 1));
            Invalidate();
        }
    }

    public MacSegmentedControl(params string[] items)
    {
        _items = items;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 36;
        BackColor = UiTheme.WindowBg;
        Font = UiTheme.Font;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        MacDraw.FillSurface(g, this);

        if (_items.Length == 0)
            return;

        float s = UiTheme.DpiScale(this);
        float pad = 3f * s;
        float h = Height - pad * 2f;
        float totalW = Width - pad * 2f;
        float segW = totalW / _items.Length;
        float radius = Math.Max(4f, 8f * s);

        // 背景胶囊槽
        var trackRect = new RectangleF(pad, pad, totalW, h);
        using (var trackPath = MacDraw.RoundedPath(trackRect, radius + 2f * s))
        using (var trackBrush = new SolidBrush(UiTheme.Current.SubCardBg))
        using (var trackPen = new Pen(UiTheme.Current.CardBorder, Math.Max(1f, 1f * s)))
        {
            g.FillPath(trackBrush, trackPath);
            g.DrawPath(trackPen, trackPath);
        }

        // 选中的药丸胶囊
        float selX = pad + _selectedIndex * segW + 2f * s;
        float selY = pad + 2f * s;
        float selW = segW - 4f * s;
        float selH = h - 4f * s;
        var selRect = new RectangleF(selX, selY, selW, selH);
        using (var selPath = MacDraw.RoundedPath(selRect, radius))
        {
            Color selBg = UiTheme.IsDark ? Color.FromArgb(0x36, 0x36, 0x3E) : Color.White;
            using var selBrush = new SolidBrush(selBg);
            g.FillPath(selBrush, selPath);

            using var selPen = new Pen(UiTheme.Current.CardBorder, 1f);
            g.DrawPath(selPen, selPath);
        }

        // 文本绘制
        for (int i = 0; i < _items.Length; i++)
        {
            float x = pad + i * segW;
            var textRect = new Rectangle((int)Math.Round(x), (int)Math.Round(pad), (int)Math.Round(segW), (int)Math.Round(h));
            bool isSel = i == _selectedIndex;
            bool isHover = i == _hoverIndex && !isSel;
            Color textColor = isSel ? UiTheme.Current.Text : (isHover ? UiTheme.Current.Text : UiTheme.Current.Dim);
            Font f = isSel ? UiTheme.FontBold : UiTheme.Font;

            TextRenderer.DrawText(g, _items[i], f, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _items.Length > 0)
        {
            float s = UiTheme.DpiScale(this);
            float pad = 3f * s;
            float segW = (Width - pad * 2f) / _items.Length;
            int idx = (int)((e.X - pad) / segW);
            if (idx >= 0 && idx < _items.Length)
                SelectedIndex = idx;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_items.Length > 0)
        {
            float s = UiTheme.DpiScale(this);
            float pad = 3f * s;
            float segW = (Width - pad * 2f) / _items.Length;
            int idx = (int)((e.X - pad) / segW);
            if (idx != _hoverIndex && idx >= 0 && idx < _items.Length)
            {
                _hoverIndex = idx;
                Invalidate();
            }
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Invalidate();
        }
    }
}

/// <summary>macOS 风格文本输入框：圆角细边框 + 内嵌真实文本框（选择/复制/输入法/剪贴板全部继承系统行为）。
/// 只读模式用于信息展示（仍可选中复制）；聚焦时边框转强调色。</summary>
internal sealed class MacField : Control
{
    private readonly TextBox _edit;

    public MacField(bool readOnly = false)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = UiTheme.CardBg;
        _edit = new TextBox
        {
            BorderStyle = BorderStyle.None,
            ReadOnly = readOnly,
            BackColor = UiTheme.CardBg,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font,
        };
        _edit.GotFocus += (_, _) =>
        {
            if (readOnly)
                _edit.SelectAll();   // 只读信息字段：键盘聚焦即全选，便于整值复制（鼠标点击仍按落点定位）
            Invalidate();
        };
        _edit.LostFocus += (_, _) => Invalidate();
        Controls.Add(_edit);
        Height = 28;   // 放在 _edit 就绪之后：改 Height 会立刻触发 OnLayout → LayoutEdit
        LayoutEdit();
    }

    /// <summary>内嵌文本框（调用方读取文本时可直接用本控件的 Text 转发）</summary>
    public TextBox Inner => _edit;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _edit.Text;
        set => _edit.Text = value ?? string.Empty;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        float s = UiTheme.DpiScale(this);
        return new Size(proposedSize.Width > 0 ? proposedSize.Width : (int)Math.Round(120f * s), (int)Math.Round(28f * s));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutEdit();
    }

    /// <summary>高 DPI 下框架缩放本控件时可能只改 Width/Height 而不触发 OnLayout ——
    /// 内嵌文本框会停在旧尺寸（实测 200% 下仅 20px 宽 → 数值被裁）。尺寸变化即重排。</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutEdit();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        LayoutEdit();
        Invalidate();
    }

    private void LayoutEdit()
    {
        float s = UiTheme.DpiScale(this);
        int pad = (int)Math.Round(8f * s);
        int h = Math.Min(_edit.PreferredHeight, Math.Max(10, Height - 2));
        var target = new Rectangle(pad, Math.Max(1, (Height - h) / 2), Math.Max(10, Width - pad * 2), h);
        if (_edit.Bounds != target)
            _edit.SetBounds(target.X, target.Y, target.Width, target.Height);   // 幂等：尺寸未变不重排
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        LayoutEdit();   // 布局自愈（幂等）：兜住框架缩放未通知的极端情形
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        MacDraw.FillSurface(g, this);

        float s = UiTheme.DpiScale(this);
        float penW = Math.Max(1f, 1f * s);
        var rect = new RectangleF(penW / 2f, penW / 2f, Width - penW, Height - penW);
        using var path = MacDraw.RoundedPath(rect, 6f * s);
        using (var fill = new SolidBrush(UiTheme.CardBg))
            g.FillPath(fill, path);
        using var border = new Pen(_edit.Focused ? UiTheme.Accent : UiTheme.ControlBorder,
            _edit.Focused ? Math.Max(1f, 1.4f * s) : penW);
        g.DrawPath(border, path);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _edit.Enabled = Enabled;
        Invalidate();
        base.OnEnabledChanged(e);
    }
}

/// <summary>macOS 风格数字输入框：圆角细边框 + 右侧步进器（上下箭头）。内嵌真实文本框（键入/选择/剪贴板/输入法全部继承系统行为）；
/// 支持 Enter 提交、Esc 还原、上下方向键步进、按住步进器连步。随主题深浅色。</summary>
internal sealed class MacNumberField : Control
{
    private const float StepperDesignW = 18f;   // 步进器宽度（设计像素，96 DPI 基线）
    private const int RepeatInitialMs = 400;   // 按住步进器：首次重复前的停顿
    private const int RepeatFastMs = 90;       // 首次重复后的连步间隔（macOS 式加速）

    private readonly TextBox _edit;
    private readonly System.Windows.Forms.Timer _repeat = new() { Interval = 400 };
    private decimal _value;
    private int _repeatDir;
    private bool _hoverUp;
    private bool _hoverDown;
    private bool _pressing;
    private float _stepWd;   // 有效步进器宽度（像素）：LayoutEdit 计算并缓存，命中/绘制共用

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Minimum { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Maximum { get; set; } = 100m;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Increment { get; set; } = 1m;

    public event EventHandler? ValueChanged;

    public MacNumberField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = UiTheme.CardBg;
        _edit = new TextBox
        {
            BorderStyle = BorderStyle.None,
            TextAlign = HorizontalAlignment.Right,
            BackColor = UiTheme.CardBg,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font,
            Text = "0",
        };
        _edit.GotFocus += (_, _) => { _edit.SelectAll(); Invalidate(); };
        _edit.LostFocus += (_, _) => { Commit(); Invalidate(); };
        _edit.KeyDown += OnEditKeyDown;
        Controls.Add(_edit);
        _repeat.Tick += (_, _) =>
        {
            if (_repeat.Interval != RepeatFastMs)
                _repeat.Interval = RepeatFastMs;   // 首次重复后加速：macOS 式按住连发
            Step(_repeatDir);
        };
        Height = 26;   // 放在 _edit 就绪之后：改 Height 会立刻触发 OnLayout → LayoutEdit
        LayoutEdit();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Value
    {
        get => _value;
        set
        {
            decimal v = Math.Clamp(value, Minimum, Maximum);
            bool changed = v != _value;
            _value = v;
            if (!_edit.Focused)
                _edit.Text = Format(v);
            if (changed)
                ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    private static string Format(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>把键入内容提交为数值（非法输入还原为当前值）</summary>
    private void Commit()
    {
        string text = _edit.Text.Trim();
        if (text.Length == 0 ||
            !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal parsed))
        {
            _edit.Text = Format(_value);
            return;
        }
        decimal v = Math.Clamp(parsed, Minimum, Maximum);
        bool changed = v != _value;
        _value = v;
        _edit.Text = Format(v);
        if (changed)
            ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void Step(int direction)
    {
        if (!Enabled)
            return;
        decimal v = Math.Clamp(_value + direction * Increment, Minimum, Maximum);
        if (v == _value)
            return;
        _value = v;
        _edit.Text = Format(v);
        if (_edit.Focused)
            _edit.SelectAll();
        ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                Commit();
                _edit.SelectAll();
                e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                _edit.Text = Format(_value);
                _edit.SelectAll();
                e.SuppressKeyPress = true;
                break;
            case Keys.Up:
                Step(1);
                e.SuppressKeyPress = true;
                break;
            case Keys.Down:
                Step(-1);
                e.SuppressKeyPress = true;
                break;
        }
    }

    /// <summary>有效步进器宽度（像素）。命中测试与绘制必须用同一宽度：窄控件回退时步进器被收窄，
    /// 若命中区仍按设计宽度算，点击文本区右侧会被误判为点步进。</summary>
    private float StepWidth => _stepWd > 0 ? _stepWd : StepperDesignW * UiTheme.DpiScale(this);

    private bool InStepper(Point p, out bool up)
    {
        up = p.Y < Height / 2f;
        return p.X >= Width - StepWidth;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left && InStepper(e.Location, out bool up))
        {
            _edit.Focus();       // 与 macOS 一致：点步进器后字段处于「已选中可键入」状态
            _edit.SelectAll();
            Step(up ? 1 : -1);
            _repeatDir = up ? 1 : -1;
            _pressing = true;
            _repeat.Interval = RepeatInitialMs;
            _repeat.Start();
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _repeat.Stop();
        _pressing = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _repeat.Stop();
        _pressing = false;
        if (_hoverUp || _hoverDown)
        {
            _hoverUp = _hoverDown = false;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool inStep = InStepper(e.Location, out bool up) && Enabled;
        bool hUp = inStep && up;
        bool hDown = inStep && !up;
        if (hUp != _hoverUp || hDown != _hoverDown)
        {
            _hoverUp = hUp;
            _hoverDown = hDown;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutEdit();
    }

    private void LayoutEdit()
    {
        float s = UiTheme.DpiScale(this);
        int pad = (int)Math.Round(8f * s);
        int stepW = (int)Math.Round(StepperDesignW * s);
        if (Width < pad * 2 + stepW + 4 * (int)s)   // 控件偏窄（如框架未随 DPI 放大 Width）：给文本至少留 3/4 宽
            stepW = Math.Max((int)Math.Round(6f * s), (Width - pad * 2) / 4);
        _stepWd = stepW;   // 命中测试与绘制共用（见 StepWidth）
        int h = Math.Min(_edit.PreferredHeight, Math.Max(10, Height - 2));
        var target = new Rectangle(pad, Math.Max(1, (Height - h) / 2), Math.Max(10, Width - pad * 2 - stepW), h);
        if (_edit.Bounds != target)
            _edit.SetBounds(target.X, target.Y, target.Width, target.Height);   // 幂等：尺寸未变不重排
    }

    /// <summary>高 DPI 下框架缩放本控件时可能只改 Width/Height 而不触发 OnLayout ——
    /// 内嵌文本框会停在旧尺寸（实测 200% 下仅 20px 宽，数值 "500" 被裁成 "50"）。</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutEdit();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        LayoutEdit();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        LayoutEdit();   // 布局自愈（幂等）：兜住框架缩放未通知的极端情形
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        MacDraw.FillSurface(g, this);

        float s = UiTheme.DpiScale(this);
        float penW = Math.Max(1f, 1f * s);
        var rect = new RectangleF(penW / 2f, penW / 2f, Width - penW, Height - penW);
        float stepperW = StepWidth;
        float radius = 6f * s;

        using var path = MacDraw.RoundedPath(rect, radius);
        using (var fill = new SolidBrush(UiTheme.CardBg))
            g.FillPath(fill, path);

        if (_hoverUp || _hoverDown)
        {
            Color hoverBg = _pressing ? UiTheme.ButtonDown : UiTheme.ButtonHover;
            g.SetClip(path);
            using var brush = new SolidBrush(hoverBg);
            if (_hoverUp)
                g.FillRectangle(brush, new RectangleF(Width - stepperW, 0, stepperW, Height / 2f));
            if (_hoverDown)
                g.FillRectangle(brush, new RectangleF(Width - stepperW, Height / 2f, stepperW, Height / 2f));
            g.ResetClip();
        }

        using (var border = new Pen(_edit.Focused ? UiTheme.Accent : UiTheme.ControlBorder,
                   _edit.Focused ? Math.Max(1f, 1.4f * s) : penW))
            g.DrawPath(border, path);

        using (var sep = new Pen(UiTheme.ControlBorder, Math.Max(1f, s)))
            g.DrawLine(sep, Width - stepperW, rect.Y + 3f * s, Width - stepperW, rect.Bottom - 3f * s);

        DrawStepperArrow(g, Width - stepperW / 2f, Height * 0.25f, -1f, s);
        DrawStepperArrow(g, Width - stepperW / 2f, Height * 0.75f, 1f, s);
    }

    private void DrawStepperArrow(Graphics g, float cx, float cy, float direction, float s)
    {
        float w = 4.2f * s;
        float h = 3.4f * s;
        PointF[] pts = direction < 0
            ? new[] { new PointF(cx - w / 2f, cy + h / 2f), new PointF(cx + w / 2f, cy + h / 2f), new PointF(cx, cy - h / 2f) }
            : new[] { new PointF(cx - w / 2f, cy - h / 2f), new PointF(cx + w / 2f, cy - h / 2f), new PointF(cx, cy + h / 2f) };
        using var brush = new SolidBrush(Enabled ? UiTheme.Text : UiTheme.Dim);
        g.FillPolygon(brush, pts);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _edit.Enabled = Enabled;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _repeat.Stop();
            _repeat.Dispose();
        }
        base.Dispose(disposing);
    }
}
