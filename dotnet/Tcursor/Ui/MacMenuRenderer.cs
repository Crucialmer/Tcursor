using System.Drawing;

namespace Tcursor.Ui;

/// <summary>托盘菜单渲染：跟随主题（浅色 = 白底蓝高亮；深色 = 深底蓝高亮，macOS 菜单观感）</summary>
internal sealed class MacMenuRenderer : ToolStripProfessionalRenderer
{
    public MacMenuRenderer()
        : base(new MacMenuColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is MacMenuHeaderItem header)
        {
            header.RenderCard(e.Graphics, e.ToolStrip);
            return;
        }

        if (e.Item.Selected && e.Item.Enabled)
        {
            float s = UiTheme.DpiScale(e.ToolStrip);
            float padX = Math.Max(5f, 6f * s);
            float padY = Math.Max(2f, 2.5f * s);
            var rect = new RectangleF(padX, padY, e.Item.Width - padX * 2f, e.Item.Height - padY * 2f);
            using var path = MacDraw.RoundedPath(rect, Math.Max(5f, 6f * s));
            using var brush = new SolidBrush(UiTheme.Current.MenuHighlightBack);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float s = UiTheme.DpiScale(e.ToolStrip);
        float margin = Math.Max(14f, 18f * s);
        int y = e.Item.Height / 2;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        using var pen = new Pen(UiTheme.Current.MenuBorder, Math.Max(1f, s));
        e.Graphics.DrawLine(pen, margin, y, e.Item.Width - margin, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(UiTheme.Current.MenuBorder, 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is MacMenuHeaderItem)
            return;

        UiPalette p = UiTheme.Current;
        e.TextColor = !e.Item.Enabled
            ? p.Dim
            : e.Item.Selected
                ? p.MenuHighlightText
                : p.MenuText;
        e.TextFormat = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine;
        base.OnRenderItemText(e);
    }

    /// <summary>勾选标记改为矢量对勾（无底块，选中态随文本反白，macOS 观感）</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is MacMenuHeaderItem)
            return;

        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Rectangle r = e.ImageRectangle;
        float s = Math.Max(1f, r.Height / 20f);
        Color checkColor = !e.Item.Enabled
            ? UiTheme.Current.Dim
            : e.Item.Selected
                ? UiTheme.Current.MenuHighlightText   // 浅色/深色选中态胶囊下纯白反白 (White Indicator)
                : UiTheme.Current.Accent;             // 未选中态为 Apple 原厂高饱和系统蓝
        using var pen = new Pen(checkColor, Math.Max(1.6f, 2.2f * s))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        var pts = new[]
        {
            new PointF(r.X + 3.5f * s, r.Y + r.Height * 0.52f),
            new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.72f),
            new PointF(r.X + r.Width - 3f * s, r.Y + r.Height * 0.28f),
        };
        g.DrawLines(pen, pts);
    }
}

/// <summary>菜单配色表：实时读取当前调色板</summary>
internal sealed class MacMenuColors : ProfessionalColorTable
{
    private static UiPalette P => UiTheme.Current;

    public override Color ToolStripDropDownBackground => P.MenuBack;
    public override Color ImageMarginGradientBegin => P.MenuBack;
    public override Color ImageMarginGradientMiddle => P.MenuBack;
    public override Color ImageMarginGradientEnd => P.MenuBack;
    public override Color MenuBorder => P.MenuBorder;
    public override Color MenuItemSelected => P.MenuHighlightBack;
    public override Color MenuItemSelectedGradientBegin => P.MenuHighlightBack;
    public override Color MenuItemSelectedGradientEnd => P.MenuHighlightBack;
    public override Color MenuItemBorder => P.MenuHighlightBack;
    public override Color MenuItemPressedGradientBegin => P.MenuBack;
    public override Color MenuItemPressedGradientEnd => P.MenuBack;
    public override Color SeparatorDark => P.MenuBorder;
    public override Color SeparatorLight => P.MenuBorder;
}
