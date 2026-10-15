using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Tcursor.Core;

namespace Tcursor.Ui;

/// <summary>
/// 托盘右键菜单顶部的专属品牌状态卡片（macOS Menu Bar Extra 风格）：
/// 位于菜单最顶部的原生状态行；
/// 第一行：左侧 18px 连续曲率方圆角（Squircle）应用图标 + 加粗排版品牌名，右侧精细磨砂半透明版本徽标胶囊；
/// 第二行：粗体 700 实时状态 + 绿/橙呼吸圆点。
/// </summary>
internal sealed class MacMenuHeaderItem : ToolStripMenuItem
{
    private bool _paused;
    private int _enabledRulesCount;
    private readonly string _versionText;

    public MacMenuHeaderItem()
    {
        AutoSize = false;
        Size = new Size(268, 58);
        ToolTipText = "点击打开光标符号设置…";
        _versionText = $"v{AppInfo.Version}";
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
        int w = Owner is not null ? UiTheme.Dpi(Owner, 268) : 268;
        int h = Owner is not null ? UiTheme.Dpi(Owner, 58) : 58;
        return new Size(w, h);
    }

    public void RenderCard(Graphics g, ToolStrip? toolStrip)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        float s = UiTheme.DpiScale(toolStrip);
        float padX = Math.Max(5f, 6f * s);
        float padY = Math.Max(2f, 3f * s);
        float w = Width - padX * 2f;
        float h = Height - padY * 2f;
        var cardRect = new RectangleF(padX, padY, w, h);
        float corner = Math.Max(6f, 7f * s);

        bool isDark = UiTheme.IsDark;
        bool hover = Selected;

        // 顶栏专属卡片底色（macOS 顶栏状态行：沉静温润衬底 + 极细边框）
        Color cardBg = isDark
            ? (hover ? Color.FromArgb(0x38, 0x38, 0x3E) : Color.FromArgb(0x2E, 0x2E, 0x33))
            : (hover ? Color.FromArgb(0xEA, 0xEA, 0xF0) : Color.FromArgb(0xF2, 0xF2, 0xF6));

        Color cardBorder = isDark
            ? Color.FromArgb(0x44, 0x44, 0x4A)
            : Color.FromArgb(0xDC, 0xDC, 0xE2);

        using (var path = MacDraw.RoundedPath(cardRect, corner))
        {
            using var brush = new SolidBrush(cardBg);
            g.FillPath(brush, path);
            using var pen = new Pen(cardBorder, Math.Max(1f, 1f * s));
            g.DrawPath(pen, path);
        }

        // 第一行：[18px 连续曲率方圆角图标] + Tcursor（加粗 Bold 700）+ [右侧版本徽标胶囊]
        float iconSize = Math.Max(16f, 18f * s);
        float iconX = cardRect.X + 10f * s;
        float line1Y = cardRect.Y + 8f * s;
        var iconRect = new RectangleF(iconX, line1Y + 1f * s, iconSize, iconSize);

        using (Bitmap? appIcon = TrayIconFactory.CreateBitmap(_paused, (int)Math.Round(iconSize)))
        {
            if (appIcon is not null)
            {
                g.DrawImage(appIcon, iconRect);
            }
            else
            {
                using var iconPath = MacDraw.RoundedPath(iconRect, Math.Max(3f, 4.5f * s));
                using var iconBrush = new SolidBrush(UiTheme.Current.Accent);
                g.FillPath(iconBrush, iconPath);
            }
        }

        // Tcursor 品牌名（Bold 700 微软雅黑，基准字阶）
        float titleX = iconRect.Right + 8f * s;
        TextRenderer.DrawText(g, "Tcursor", UiTheme.FontMenuBold,
            new Point((int)Math.Round(titleX), (int)Math.Round(line1Y)),
            UiTheme.Current.Text, TextFormatFlags.NoPadding);

        // 精细磨砂半透明版本徽标胶囊（对标 SwitchTask #menuHeaderVersion）
        Size verTextSize = TextRenderer.MeasureText(_versionText, UiTheme.FontCaption);
        float verBadgeW = verTextSize.Width + 10f * s;
        float verBadgeH = Math.Max(16f, 18f * s);
        float verBadgeX = cardRect.Right - verBadgeW - 10f * s;
        float verBadgeY = line1Y + 1f * s;
        var verRect = new RectangleF(verBadgeX, verBadgeY, verBadgeW, verBadgeH);
        using (var verPath = MacDraw.RoundedPath(verRect, Math.Max(3f, 4f * s)))
        {
            Color verBg = isDark ? Color.FromArgb(32, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
            using var verBrush = new SolidBrush(verBg);
            g.FillPath(verBrush, verPath);
        }
        var verTextRect = new Rectangle((int)verBadgeX, (int)verBadgeY, (int)verBadgeW, (int)verBadgeH);
        TextRenderer.DrawText(g, _versionText, UiTheme.FontCaption, verTextRect, UiTheme.Current.Dim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // 第二行：状态呼吸圆点 + 实时状态（粗体 700 + 绿/橙呼吸圆点）
        float line2Y = cardRect.Y + 31f * s;
        Color dotColor = _paused ? UiTheme.Current.TrafficYellow : UiTheme.Current.TrafficGreen;
        float dotDiameter = Math.Max(6f, 6.5f * s);
        float dotX = iconX + 1.5f * s;
        float dotY = line2Y + 4f * s;

        using (var dotBrush = new SolidBrush(dotColor))
            g.FillEllipse(dotBrush, dotX, dotY, dotDiameter, dotDiameter);

        string statusText = _paused
            ? "已暂停 · 光标符号已隐藏"
            : (_enabledRulesCount > 0 ? $"运行中 · 白名单已匹配 {_enabledRulesCount} 项" : "运行中 · 光标捕获就绪");

        float statusX = dotX + dotDiameter + 6f * s;
        var statusRect = new Rectangle(
            (int)Math.Round(statusX),
            (int)Math.Round(line2Y),
            Math.Max(0, (int)Math.Round(cardRect.Right - statusX - 8f * s)),
            (int)Math.Round(18f * s));

        TextRenderer.DrawText(g, statusText, UiTheme.FontMenuBold,
            statusRect,
            UiTheme.Current.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}
