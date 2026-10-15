using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Tcursor.Ui;

/// <summary>
/// 托盘右键菜单顶部的专属品牌状态卡片（macOS Menu Bar Extra 风格）。
/// 包含应用徽标、Tcursor 粗体品牌、版本标签，以及呼吸圆点和实时运行状态。
/// </summary>
internal sealed class MacMenuHeaderItem : ToolStripMenuItem
{
    private bool _paused;
    private int _enabledRulesCount;
    private readonly string _versionText;

    public MacMenuHeaderItem()
    {
        AutoSize = false;
        ToolTipText = "点击打开光标符号设置…";
        var ver = typeof(MacMenuHeaderItem).Assembly.GetName().Version;
        _versionText = ver is not null ? $"v{ver.Major}.{ver.Minor}" : "v2.5";
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (Owner is not null)
            Owner.Cursor = Cursors.Hand;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (Owner is not null)
            Owner.Cursor = Cursors.Default;
    }

    public void UpdateStatus(bool paused, int enabledRulesCount)
    {
        if (_paused != paused || _enabledRulesCount != enabledRulesCount)
        {
            _paused = paused;
            _enabledRulesCount = enabledRulesCount;
            Invalidate();
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        return new Size(UiTheme.Dpi(Owner, 224), UiTheme.Dpi(Owner, 54));
    }

    public void RenderCard(Graphics g, ToolStrip? toolStrip)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        float s = UiTheme.DpiScale(toolStrip);
        float padX = Math.Max(4f, 5f * s);
        float padY = Math.Max(2f, 3f * s);
        float w = Width - padX * 2f;
        float h = Height - padY * 2f;
        var cardRect = new RectangleF(padX, padY, w, h);
        float corner = Math.Max(5f, 6.5f * s);

        bool isDark = UiTheme.IsDark;
        bool hover = Selected;

        // 卡片底色与边框（macOS 顶栏卡片：微差衬底 + 极细边框）
        Color cardBg = isDark
            ? (hover ? Color.FromArgb(0x3C, 0x3C, 0x42) : Color.FromArgb(0x32, 0x32, 0x36))
            : (hover ? Color.FromArgb(0xE8, 0xE8, 0xEE) : Color.FromArgb(0xF3, 0xF3, 0xF6));

        Color cardBorder = isDark
            ? Color.FromArgb(0x44, 0x44, 0x4A)
            : Color.FromArgb(0xE0, 0xE0, 0xE6);

        using (var path = MacDraw.RoundedPath(cardRect, corner))
        {
            using var brush = new SolidBrush(cardBg);
            g.FillPath(brush, path);
            using var pen = new Pen(cardBorder, Math.Max(1f, 1f * s));
            g.DrawPath(pen, path);
        }

        // 左侧图标徽章（Apple Accent Blue 连续圆角底块 + 白色 T 光标符号）
        float badgeSize = Math.Max(24f, 30f * s);
        float badgeX = cardRect.X + 8f * s;
        float badgeY = cardRect.Y + (h - badgeSize) / 2f;
        var badgeRect = new RectangleF(badgeX, badgeY, badgeSize, badgeSize);
        float badgeCorner = Math.Max(4f, 6f * s);

        using (var badgePath = MacDraw.RoundedPath(badgeRect, badgeCorner))
        {
            using var badgeBrush = new SolidBrush(_paused ? UiTheme.Current.Dim : UiTheme.Current.Accent);
            g.FillPath(badgeBrush, badgePath);
        }

        // 绘制徽章内部的 "T" 字符
        using (var tFont = new Font(UiTheme.PrimaryFontFamily, 11f * s, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            var tRect = new Rectangle((int)badgeRect.X, (int)badgeRect.Y, (int)badgeRect.Width, (int)badgeRect.Height);
            TextRenderer.DrawText(g, "T", tFont, tRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // 右侧文字排版区域
        float textX = badgeRect.Right + 9f * s;
        float rightPadding = cardRect.Right - 8f * s;

        // 第一行：Tcursor（Bold 700 品牌名）+ 版本微标
        int line1Y = (int)Math.Round(cardRect.Y + 7f * s);
        TextRenderer.DrawText(g, "Tcursor", UiTheme.FontMenuHeader,
            new Point((int)textX, line1Y), UiTheme.Current.Text, TextFormatFlags.NoPadding);

        Size brandSize = TextRenderer.MeasureText("Tcursor", UiTheme.FontMenuHeader);
        int verX = (int)textX + brandSize.Width + (int)Math.Round(6f * s);
        TextRenderer.DrawText(g, _versionText, UiTheme.FontCaption,
            new Point(verX, line1Y + (int)Math.Round(2f * s)), UiTheme.Current.Dim, TextFormatFlags.NoPadding);

        // 第二行：状态呼吸圆点 + 状态文字
        int line2Y = (int)Math.Round(cardRect.Y + 28f * s);
        Color dotColor = _paused ? UiTheme.Current.TrafficYellow : UiTheme.Current.TrafficGreen;
        float dotDiameter = Math.Max(6f, 6.5f * s);
        float dotX = textX + 1f * s;
        float dotY = line2Y + 3f * s;

        using (var dotBrush = new SolidBrush(dotColor))
            g.FillEllipse(dotBrush, dotX, dotY, dotDiameter, dotDiameter);

        string statusText = _paused
            ? "已暂停 · 光标提示隐藏"
            : (_enabledRulesCount > 0 ? $"运行中 · {_enabledRulesCount} 条规则已启用" : "运行中 · 光标捕获就绪");

        int statusX = (int)Math.Round(dotX + dotDiameter + 5f * s);
        int statusW = Math.Max(0, (int)(rightPadding - statusX));
        TextRenderer.DrawText(g, statusText, UiTheme.FontCaption,
            new Rectangle(statusX, line2Y, statusW, (int)(16f * s)),
            UiTheme.Current.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}
