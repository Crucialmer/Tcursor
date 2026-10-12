using System.Drawing.Drawing2D;

namespace Tcursor.Ui;

/// <summary>macOS 风格控件的公共绘制工具</summary>
internal static class MacDraw
{
    /// <summary>连续曲率圆角矩形路径（Apple/Figma 算法，与符号圆角同一几何）</summary>
    public static GraphicsPath RoundedPath(RectangleF rect, float radius)
        => Render.SymbolRenderer.SmoothRectPath(rect.X, rect.Y, rect.Width, rect.Height, radius, 0.6f);

    public static void FillSurface(Graphics g, Control c)
    {
        using var brush = new SolidBrush(c.BackColor);
        g.FillRectangle(brush, c.ClientRectangle);
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
        else if (_swatch.HasValue)
        {
            fill = _down ? ControlPaint.Dark(_swatch.Value, 0.06f) : _hover ? ControlPaint.Light(_swatch.Value, 0.14f) : _swatch.Value;
            borderColor = Color.FromArgb(60, 0, 0, 0);
        }
        else if (Accent)
            fill = _down ? UiTheme.AccentDown : _hover ? UiTheme.AccentHover : UiTheme.Accent;
        else
            fill = _down ? UiTheme.ButtonDown : _hover ? UiTheme.ButtonHover : UiTheme.ButtonFace;

        using (var path = MacDraw.RoundedPath(rect, _swatch.HasValue ? UiTheme.DpiF(this, 6f) : UiTheme.DpiF(this, 8f)))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
            if (!Accent || _swatch.HasValue)
            {
                using var pen = new Pen(borderColor, penW);
                g.DrawPath(pen, path);
            }
        }

        Color textColor = !Enabled
            ? UiTheme.Dim
            : Accent && !_swatch.HasValue
                ? Color.White
                : _swatch.HasValue && _swatch.Value.GetBrightness() < 0.62f
                    ? Color.White
                    : UiTheme.Text;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _down = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
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
        var circle = new RectangleF(1.5f * s, cy - 9f * s, 18f * s, 18f * s);
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

        int textX = (int)Math.Round(25f * s);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textX, 0, Width - textX, Height),
            Enabled ? UiTheme.Text : UiTheme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
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
        var box = new RectangleF(1.5f * s, cy - 9f * s, 18f * s, 18f * s);
        using (var path = MacDraw.RoundedPath(box, 5f * s))
        {
            if (Checked)
            {
                using var brush = new SolidBrush(Enabled ? UiTheme.Accent : UiTheme.Dim);
                g.FillPath(brush, path);
                using var pen = new Pen(Color.White, Math.Max(1.5f, 2.2f * s))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };
                var pts = new[]
                {
                    new PointF(box.X + 4.6f * s, box.Y + 9.6f * s),
                    new PointF(box.X + 7.6f * s, box.Y + 12.6f * s),
                    new PointF(box.X + 13.6f * s, box.Y + 5.6f * s),
                };
                g.DrawLines(pen, pts);
            }
            else
            {
                using var brush = new SolidBrush(BackColor);   // 未选中面 = 所在表面色，随主题
                g.FillPath(brush, path);
                using var pen = new Pen(UiTheme.ControlBorder, Math.Max(1f, 1.2f * s));
                g.DrawPath(pen, path);
            }
        }

        int textX = (int)Math.Round(25f * s);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textX, 0, Width - textX, Height),
            Enabled ? UiTheme.Text : UiTheme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
}

/// <summary>macOS 风格开关（药丸 + 圆钮，随主题深浅色）</summary>
internal sealed class MacSwitch : CheckBox
{
    private const int TrackW = 42;   // 设计像素（96 DPI 基线）
    private const int TrackH = 24;

    private int TrackWpx => UiTheme.Dpi(this, TrackW);
    private int TrackHpx => UiTheme.Dpi(this, TrackH);

    public MacSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        BackColor = UiTheme.WindowBg;
        Font = UiTheme.Font;
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
        float knobX = Checked ? track.Right - knobD - 2f * s : track.X + 2f * s;
        using (var knob = new GraphicsPath())
        {
            knob.AddEllipse(knobX, track.Y + 2f * s, knobD, knobD);
            using var brush = new SolidBrush(Color.White);
            g.FillPath(brush, knob);
            using var pen = new Pen(Color.FromArgb(28, 0, 0, 0), Math.Max(1f, s));
            g.DrawPath(pen, knob);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            int textX = trackW + (int)Math.Round(9f * s);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(textX, 0, Width - textX, Height),
                Enabled ? UiTheme.Text : UiTheme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
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
        if (_dragging)
            SetFromMouseX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        base.OnMouseUp(e);
    }
}

/// <summary>macOS 风格卡片容器（圆角白底 + 细边框 + 可选标题；随主题深浅色）</summary>
internal sealed class MacCard : Panel
{
    public string? Title { get; }

    public MacCard(string? title = null)
    {
        Title = title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.WindowBg;
        Font = UiTheme.Font;
        Padding = title is null ? new Padding(14, 14, 14, 14) : new Padding(14, 34, 14, 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        MacDraw.FillSurface(g, this);

        float penW = Math.Max(1f, UiTheme.DpiF(this, 1f));
        var rect = new RectangleF(penW / 2f, penW / 2f, Width - penW, Height - penW);
        using (var path = MacDraw.RoundedPath(rect, UiTheme.DpiF(this, 10f)))
        {
            using var fill = new SolidBrush(UiTheme.CardBg);
            g.FillPath(fill, path);
            using var pen = new Pen(UiTheme.CardBorder, penW);
            g.DrawPath(pen, path);
        }

        if (!string.IsNullOrEmpty(Title))
        {
            TextRenderer.DrawText(g, Title, UiTheme.FontBold,
                new Point(UiTheme.Dpi(this, 15), UiTheme.Dpi(this, 10)), UiTheme.SectionTitle, TextFormatFlags.NoPadding);
        }
    }

}
