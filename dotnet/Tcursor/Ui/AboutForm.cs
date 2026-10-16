using System.Drawing.Drawing2D;
using Tcursor.Core;

namespace Tcursor.Ui;

/// <summary>「关于」对话框（macOS 26 版式：光影方圆角应用图标 + 居中品牌名称 + 极光药丸版本徽标 + 许可说明；自适应深浅色与 8K 极清）</summary>
internal sealed class AboutForm : MacDialogForm
{
    private readonly PictureBox _icon = new()
    {
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = UiTheme.WindowBg,
        Anchor = AnchorStyles.None,
        Margin = new Padding(0, 4, 0, 10),
    };
    private Bitmap? _iconBitmap;

    public AboutForm()
    {
        SuspendLayout();
        Text = "关于 Tcursor";
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(450, 460 + MacDialogForm.HeaderDesignPx);
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        var name = new Label
        {
            Text = "Tcursor",
            AutoSize = true,
            Font = UiTheme.FontHero,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.WindowBg,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 4),
        };

        // 药丸版本徽标卡片
        var version = new Label
        {
            Text = $"v{AppInfo.Version} · macOS 26 Edition",
            AutoSize = true,
            Font = UiTheme.FontCaptionBold,
            ForeColor = UiTheme.Accent,
            BackColor = UiTheme.SubCardBg,
            Anchor = AnchorStyles.None,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 0, 14),
        };

        var desc = new Label
        {
            Text = "智能光标输入法状态提示" + Environment.NewLine +
                   "中文 / 英文 / 大写 / 英文键盘 / 日文 / 韩文" + Environment.NewLine +
                   "白名单规则 · 实时窗口侦测 · 四级捕获链 · 静默无感更新",
            AutoSize = true,
            Font = UiTheme.Font,
            MaximumSize = new Size(380, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.WindowBg,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 14),
        };

        var license = new Label
        {
            Text = "GNU 通用公共许可证第 3 版（GPL-3.0）" + Environment.NewLine +
                   "© 2026 Crucialmer · 保持开源与自由" + Environment.NewLine +
                   "第三方开源组件许可见 THIRD-PARTY-NOTICES.txt",
            AutoSize = true,
            Font = UiTheme.FontCaption,
            MaximumSize = new Size(380, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = UiTheme.Dim,
            BackColor = UiTheme.WindowBg,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 8),
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16, 10, 16, 6),
            BackColor = UiTheme.WindowBg,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (Control control in new Control[] { _icon, name, version, desc, license })
        {
            content.RowCount++;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(control, 0, content.RowCount - 1);
        }

        var ok = UiTheme.Button("确定", accent: true, width: 104);
        ok.DialogResult = DialogResult.OK;
        var check = UiTheme.Button("检查更新…", width: 120);
        check.Click += (_, _) =>
        {
            DialogResult = DialogResult.Abort;
            Close();
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(8, 6, 8, 8),
            Dock = DockStyle.Fill,
            BackColor = UiTheme.WindowBg,
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(check);
        AcceptButton = ok;
        CancelButton = ok;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 12, 16, 14), BackColor = UiTheme.WindowBg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(content, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        Load += (_, _) =>
        {
            int size = UiTheme.Dpi(this, 84);
            _icon.Size = new Size(size, size);
            _iconBitmap = CreateDecoratedIcon(size);
            _icon.Image = _iconBitmap;
            MacDialogForm.PositionBottomRight(this);
        };

        ResumeLayout(false);
        PerformLayout();
    }

    private static Bitmap CreateDecoratedIcon(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using Bitmap? rawIcon = TrayIconFactory.CreateBitmap(paused: false, size);
        if (rawIcon is not null)
        {
            var rect = new RectangleF(0, 0, size, size);
            using var path = MacDraw.RoundedPath(rect, size * 0.22f);
            g.SetClip(path);
            g.DrawImage(rawIcon, rect);
            g.ResetClip();

            // 0.5px 极细微高光描边
            using var borderPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1.2f);
            g.DrawPath(borderPen, path);
        }
        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Image = null;
            _iconBitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
