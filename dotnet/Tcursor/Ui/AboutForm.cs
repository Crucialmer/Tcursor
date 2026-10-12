using Tcursor.Core;

namespace Tcursor.Ui;

/// <summary>「关于」对话框（macOS 风格窗口 + 卡片，随主题深浅色；替代系统 MessageBox）</summary>
internal sealed class AboutForm : MacDialogForm
{
    public AboutForm()
    {
        SuspendLayout();
        Text = "关于 Tcursor";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 300 + MacDialogForm.HeaderDesignPx);
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        var title = new Label
        {
            Text = "Tcursor",
            AutoSize = true,
            Font = UiTheme.FontBold,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.CardBg,
            Margin = new Padding(0, 2, 0, 0),
        };
        var version = new Label
        {
            Text = "版本 " + AppInfo.Version,
            AutoSize = true,
            ForeColor = UiTheme.Dim,
            BackColor = UiTheme.CardBg,
            Margin = new Padding(0, 6, 0, 0),
        };
        var desc = new Label
        {
            Text = "在光标旁显示输入法状态符号（中/英/大写/英键/日/韩）。\r\n" +
                   "六种状态检测 · 白名单规则 · 窗口信息 · GUI/UIA/MSAA 捕获链。",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.CardBg,
            Margin = new Padding(0, 16, 0, 0),
        };
        var license = new Label
        {
            Text = "许可：AGPL-3.0；基于 abgox/InputTip；第三方组件见 THIRD-PARTY-NOTICES.txt",
            AutoSize = true,
            ForeColor = UiTheme.Dim,
            BackColor = UiTheme.CardBg,
            Margin = new Padding(0, 16, 0, 0),
        };
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = UiTheme.CardBg,
        };
        body.Controls.Add(title);
        body.Controls.Add(version);
        body.Controls.Add(desc);
        body.Controls.Add(license);
        var card = new MacCard { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(20) };
        card.Controls.Add(body);

        var ok = UiTheme.Button("确定", accent: true, width: 96);
        ok.DialogResult = DialogResult.OK;
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
            Dock = DockStyle.Fill,
        };
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = ok;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16), BackColor = UiTheme.WindowBg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        ResumeLayout(false);
        PerformLayout();
    }
}
