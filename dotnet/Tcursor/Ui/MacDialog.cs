namespace Tcursor.Ui;

/// <summary>macOS 风格消息对话框（替代系统 MessageBox；随主题深浅色、居中于父窗体）</summary>
internal static class MacDialog
{
    /// <summary>确认对话框（取消 / 确定）；点「确定」返回 true</summary>
    public static bool Confirm(IWin32Window? owner, string message, string title = "确认")
    {
        using var form = new MacMessageForm(message, title, confirm: true);
        return Show(owner, form) == DialogResult.OK;
    }

    /// <summary>提示对话框（单个「确定」按钮）</summary>
    public static void Alert(IWin32Window? owner, string message, string title = "提示")
    {
        using var form = new MacMessageForm(message, title, confirm: false);
        _ = Show(owner, form);
    }

    private static DialogResult Show(IWin32Window? owner, MacMessageForm form)
        => owner is null ? form.ShowDialog() : form.ShowDialog(owner);
}

/// <summary>消息对话框窗体（由 MacDialog 使用；自绘标题区 + 文本 + 按钮）</summary>
internal sealed class MacMessageForm : MacDialogForm
{
    private const int MessageWidth = 360;   // 文本最大宽度（设计像素）
    private readonly Label _message;

    public MacMessageForm(string message, string title, bool confirm)
    {
        SuspendLayout();
        Text = title;
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        _message = new Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Size(MessageWidth, 0),   // 值随 DPI 由框架缩放
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.WindowBg,
            Margin = new Padding(0),
        };
        var textHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.WindowBg };
        textHost.Controls.Add(_message);

        var ok = UiTheme.Button("确定", accent: true, width: 96);
        ok.DialogResult = DialogResult.OK;
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(0, 4, 0, 0),
            Dock = DockStyle.Fill,
        };
        buttons.Controls.Add(ok);
        MacButton? cancel = null;
        if (confirm)
        {
            cancel = UiTheme.Button("取消", width: 96);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);
        }
        AcceptButton = ok;
        CancelButton = cancel ?? ok;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20, 14, 20, 14), BackColor = UiTheme.WindowBg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(textHost, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        // 占位尺寸：实际高度在 OnLoad（DeviceDpi 就绪）按设备单位重算，避免测量与 AutoScale 双重换算
        ClientSize = new Size(400, 132 + MacDialogForm.HeaderDesignPx);

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // MeasureText 的 proposed 尺寸是设备像素：此处按当前 DPI 换算宽度，测量单位与布局完全一致
        Size textSize = TextRenderer.MeasureText(_message.Text, _message.Font,
            new Size(UiTheme.Dpi(this, MessageWidth), int.MaxValue), TextFormatFlags.WordBreak);
        int contentHeight = UiTheme.Dpi(this, 14 + 12 + 40 + 14) + textSize.Height;
        ClientSize = new Size(ClientSize.Width, contentHeight + UiTheme.Dpi(this, MacDialogForm.HeaderDesignPx));
    }
}
