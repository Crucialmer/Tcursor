using System.Text.RegularExpressions;
using Tcursor.Config;
using Tcursor.Core;

namespace Tcursor.Ui;

/// <summary>
/// 「窗口信息」面板（macOS 26 仪表盘风格）：实时动态探测前台窗口，
/// 并提供一键规则录入与属性复制。全矢量高 DPI 适配（最高 8K 显示器）。
/// </summary>
internal sealed class WindowInfoForm : MacDialogForm
{
    private readonly AppConfig _config;
    private readonly Action _applyConfig;

    private readonly MacField _process = MakeField(mono: true);
    private readonly MacField _rootClass = MakeField(mono: true);
    private readonly MacField _focusClass = MakeField(mono: true);
    private readonly MacField _title = MakeField(mono: false);
    private readonly MacField _path = MakeField(mono: true);
    private readonly Label _status = new() { AutoSize = true, Font = UiTheme.FontCaptionBold, ForeColor = UiTheme.Dim, Margin = new Padding(6, 6, 6, 0) };
    private readonly MacButton _addButton = new(accent: true) { Text = "✨ 添加为白名单规则", Width = 150, Height = 34, Enabled = false };
    private readonly MacButton _copyButton = new() { Text = "拷贝进程名", Width = 110, Height = 34, Enabled = false };
    private readonly MacButton _copyPathButton = new() { Text = "拷贝路径", Width = 96, Height = 34, Enabled = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 120 };
    private const int PathCacheTtlMs = 3000;

    private string _lastProcess = "";
    private uint _pathPid;
    private string _pathValue = "";
    private long _pathAt;

    public WindowInfoForm(AppConfig config, Action applyConfig)
    {
        SuspendLayout();
        _config = config;
        _applyConfig = applyConfig;

        Text = "窗口信息 - Tcursor";
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(620, 440 + MacDialogForm.HeaderDesignPx);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16, 12, 16, 14),
            BackColor = UiTheme.WindowBg,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // Tip
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Cards
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // Status & Buttons

        var tip = new Label
        {
            Text = "● 实时侦测中：请直接切换至任意前台应用窗口，下方信息将自动更新",
            AutoSize = true,
            Font = UiTheme.FontCaptionBold,
            ForeColor = UiTheme.Accent,
            Margin = new Padding(4, 0, 4, 10),
            BackColor = UiTheme.WindowBg,
        };
        root.Controls.Add(tip, 0, 0);

        // 主信息卡片
        var card = new MacCard("前台窗口属性") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            BackColor = UiTheme.CardBg,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int row = 0;
        void AddRow(string label, Control control)
        {
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, row);
            table.Controls.Add(control, 1, row);
            row++;
        }

        AddRow("进程名称", _process);
        AddRow("窗口标题", _title);
        AddRow("进程路径", _path);
        AddRow("顶层类名", _rootClass);
        AddRow("焦点类名", _focusClass);
        card.Controls.Add(table);
        root.Controls.Add(card, 0, 1);

        // 底部动作行
        var bottomBox = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };

        _addButton.Click += (_, _) => AddRuleFromCurrent();
        _copyButton.Click += (_, _) => CopyProcessName();
        _copyPathButton.Click += (_, _) => CopyPathName();
        var close = UiTheme.Button("关闭", width: 88);
        close.Click += (_, _) => Close();

        bottomBox.Controls.Add(_addButton);
        bottomBox.Controls.Add(_copyButton);
        bottomBox.Controls.Add(_copyPathButton);
        bottomBox.Controls.Add(close);
        bottomBox.Controls.Add(_status);

        root.Controls.Add(bottomBox, 0, 2);
        Controls.Add(root);

        _timer.Tick += (_, _) => RefreshInfo();
        Load += (_, _) =>
        {
            // 依据当前 DPI 缩放首列宽度
            table.ColumnStyles[0].Width = UiTheme.Dpi(this, 90);
            MacDialogForm.PositionBottomRight(this);
        };
        Shown += (_, _) =>
        {
            RefreshInfo();
            _timer.Start();
        };
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
        };

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }

    private static MacField MakeField(bool mono = false)
    {
        var field = new MacField(readOnly: true)
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.CardBg,
            Margin = new Padding(0, 3, 0, 5),
            Height = 30,
        };
        if (mono)
            field.Inner.Font = UiTheme.FontMono;
        return field;
    }

    private void RefreshInfo()
    {
        if (IsDisposed)
            return;
        IntPtr fg = Win32.NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
            return;
        Win32.NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
            return;

        long now = Environment.TickCount64;
        bool refreshPath = pid != _pathPid || now - _pathAt >= PathCacheTtlMs;

        WindowProbeResult info = WindowProbe.GetInfo(fg, includePath: refreshPath);
        if (refreshPath)
        {
            _pathPid = pid;
            _pathAt = now;
            _pathValue = info.Path;
        }
        _process.Text = info.Process;
        _rootClass.Text = info.RootClass;
        _focusClass.Text = info.FocusClass;
        _title.Text = info.Title;
        _path.Text = _pathValue;

        if (info.Process.Length > 0)
        {
            _lastProcess = info.Process;
            _addButton.Enabled = true;
            _copyButton.Enabled = true;
            _copyPathButton.Enabled = !string.IsNullOrEmpty(_pathValue);
        }
    }

    private void AddRuleFromCurrent()
    {
        string process = _lastProcess;
        if (process.Length == 0)
            return;

        string pattern = Regex.Escape(process);
        foreach (RuleConfig r in _config.Rules)
        {
            if (string.Equals(r.Process, pattern, StringComparison.OrdinalIgnoreCase))
            {
                _status.Text = $"已存在规则「{process}」";
                return;
            }
        }

        _config.Rules.Add(new RuleConfig
        {
            Name = process,
            Process = pattern,
            Enabled = true,
        });
        _applyConfig();

        _status.Text = $"已成功添加白名单规则：「{process}」";
    }

    private void CopyProcessName()
    {
        if (_lastProcess.Length == 0)
            return;
        try
        {
            Clipboard.SetText(_lastProcess);
            _status.Text = $"已复制进程名：「{_lastProcess}」";
        }
        catch (Exception ex)
        {
            _status.Text = "复制失败：" + ex.Message;
        }
    }

    private void CopyPathName()
    {
        if (string.IsNullOrEmpty(_pathValue))
            return;
        try
        {
            Clipboard.SetText(_pathValue);
            _status.Text = "已复制可执行文件路径";
        }
        catch (Exception ex)
        {
            _status.Text = "复制失败：" + ex.Message;
        }
    }
}
