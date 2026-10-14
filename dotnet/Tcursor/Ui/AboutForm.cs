using Tcursor.Core;

namespace Tcursor.Ui;

/// <summary>「关于」对话框（macOS 版式：应用图标 + 居中名称/版本 + 说明 + 许可；随主题深浅色）</summary>
internal sealed class AboutForm : MacDialogForm
{
    private readonly PictureBox _icon = new()
    {
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = UiTheme.WindowBg,
        Anchor = AnchorStyles.None,
        Margin = new Padding(0, 2, 0, 12),
    };
    private Bitmap? _iconBitmap;

    public AboutForm()
    {
        SuspendLayout();
        Text = "关于 Tcursor";
        StartPosition = FormStartPosition.Manual;   // 位置在 Load 里贴右下角（托盘区）
        ClientSize = new Size(420, 360 + MacDialogForm.HeaderDesignPx);
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
            Font = UiTheme.FontTitle,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.WindowBg,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 4),
        };
        var version = new Label
        {
            Text = "版本 " + AppInfo.Version,
            AutoSize = true,
            ForeColor = UiTheme.Dim,
            BackColor = UiTheme.WindowBg,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 14),
        };
        var desc = new Label
        {
            Text = "在光标旁显示输入法状态符号：" + Environment.NewLine +
                   "中文 / 英文 / 大写 / 英文键盘 / 日文 / 韩文" + Environment.NewLine +
                   "白名单规则 · 窗口信息 · 三种捕获链 · 静默自动更新。",
            AutoSize = true,
            MaximumSize = new Size(340, 0),   // 限宽换行；值随 DPI 由框架缩放
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.WindowBg,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 12),
        };
        var license = new Label
        {
            Text = "GNU 通用公共许可证第 3 版（GPL-3.0）" + Environment.NewLine +
                   "© 2026 Crucialmer · 第三方组件见 THIRD-PARTY-NOTICES.txt",
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = UiTheme.Dim,
            BackColor = UiTheme.WindowBg,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0),
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16, 18, 16, 6),
            BackColor = UiTheme.WindowBg,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (Control control in new Control[] { _icon, name, version, desc, license })
        {
            content.RowCount++;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(control, 0, content.RowCount - 1);
        }

        var ok = UiTheme.Button("确定", accent: true, width: 96);
        ok.DialogResult = DialogResult.OK;
        var check = UiTheme.Button("检查更新…", width: 118);
        check.Click += (_, _) =>
        {
            DialogResult = DialogResult.Abort;   // 由调用方（托盘应用）接续执行检查更新
            Close();
        };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
            Dock = DockStyle.Fill,
            BackColor = UiTheme.WindowBg,
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(check);
        AcceptButton = ok;
        CancelButton = ok;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16), BackColor = UiTheme.WindowBg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(content, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        Load += (_, _) =>
        {
            int size = UiTheme.Dpi(this, 76);
            _icon.Size = new Size(size, size);
            _iconBitmap = TrayIconFactory.CreateBitmap(paused: false, size);
            _icon.Image = _iconBitmap;
            MacDialogForm.PositionBottomRight(this);   // 与其它「提示 / 信息」类窗口一致：贴托盘区
        };

        ResumeLayout(false);
        PerformLayout();
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
