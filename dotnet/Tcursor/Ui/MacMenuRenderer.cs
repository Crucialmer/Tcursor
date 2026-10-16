using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Tcursor.Ui;

/// <summary>托盘菜单渲染：跟随主题（浅色 = 白底蓝高亮；深色 = 深底蓝高亮，macOS 菜单观感）</summary>
internal sealed class MacMenuRenderer : ToolStripProfessionalRenderer
{
    public MacMenuRenderer()
        : base(new MacMenuColors())
    {
        RoundedEdges = false;
    }

    /// <summary>清空系统默认左侧灰色图像边栏，保证整个菜单底色一体纯净</summary>
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // 不绘制灰色槽条，保持通透
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
            float padX = Math.Max(4f, 4f * s);
            float padY = Math.Max(1.5f, 1.5f * s);
            var rect = new RectangleF(padX, padY, e.Item.Width - padX * 2f, e.Item.Height - padY * 2f);
            using var path = MacDraw.RoundedPath(rect, Math.Max(4f, 5f * s));
            using var brush = new SolidBrush(UiTheme.Current.MenuHighlightBack);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float s = UiTheme.DpiScale(e.ToolStrip);
        float margin = Math.Max(10f, 12f * s);
        int y = e.Item.Height / 2;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(UiTheme.Current.MenuBorder, Math.Max(1f, 1f * s));
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

        float s = UiTheme.DpiScale(e.ToolStrip);
        // 统一基准缩进列（30px 标尺）：与左侧对勾保持优雅间距
        float textX = Math.Max(26f, 30f * s);
        var textRect = new Rectangle(
            (int)Math.Round(textX),
            0,
            Math.Max(0, (int)Math.Round(e.Item.Width - textX - 10f * s)),
            e.Item.Height);

        UiPalette p = UiTheme.Current;
        Color textColor = !e.Item.Enabled
            ? p.Dim
            : e.Item.Selected
                ? p.MenuHighlightText
                : p.MenuText;

        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            textRect,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    /// <summary>勾选标记改为矢量对勾（居中于胶囊内部，距左 13px，选中态纯白反白，macOS 原生观感）</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is MacMenuHeaderItem)
            return;

        float s = UiTheme.DpiScale(e.ToolStrip);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 垂直居中、距左 13px
        float checkW = Math.Max(8.5f, 9.5f * s);
        float checkH = Math.Max(7f, 8f * s);
        float x = Math.Max(11f, 13f * s);
        float y = (e.Item.Height - checkH) / 2f;

        Color checkColor = !e.Item.Enabled
            ? UiTheme.Current.Dim
            : e.Item.Selected
                ? UiTheme.Current.MenuHighlightText   // 浅色/深色选中态胶囊下纯白反白 (White Indicator)
                : UiTheme.Current.Accent;             // 未选中态为 Apple 原厂高饱和系统蓝

        using var pen = new Pen(checkColor, Math.Max(1.6f, 2.0f * s))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var pts = new[]
        {
            new PointF(x, y + checkH * 0.48f),
            new PointF(x + checkW * 0.38f, y + checkH),
            new PointF(x + checkW, y + 1f * s),
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
