using System.Text.RegularExpressions;
using Tcursor.Config;
using Tcursor.Core;

namespace Tcursor.Ui;

/// <summary>
/// 「窗口信息」面板：实时显示当前选中（前台）窗口的信息，
/// 并可一键把该窗口的进程添加为白名单规则。
/// </summary>
internal sealed class WindowInfoForm : MacDialogForm
{
    private readonly AppConfig _config;
    private readonly Action _applyConfig;

    private readonly TextBox _process = MakeBox();
    private readonly TextBox _rootClass = MakeBox();
    private readonly TextBox _focusClass = MakeBox();
    private readonly TextBox _title = MakeBox();
    private readonly TextBox _path = MakeBox();
    private readonly Label _status = new() { AutoSize = true, ForeColor = UiTheme.Dim, Margin = new Padding(10, 4, 10, 0) };
    private readonly MacButton _addButton = new(accent: true) { Text = "添加为白名单规则(&A)", Width = 168, Height = 32, Enabled = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 120 };

    private string _lastProcess = "";

    public WindowInfoForm(AppConfig config, Action applyConfig)
    {
        SuspendLayout();   // 设计器惯例：构造期间挂起布局，末段 ResumeLayout+PerformLayout 时统一触发 AutoScale 落地
        _config = config;
        _applyConfig = applyConfig;

        Text = "窗口信息 - Tcursor";
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);   // 设计基线 96 DPI：缺失该值会使 AutoScaleFactor 恒为 1、高 DPI 完全不缩放
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 320 + MacDialogForm.HeaderDesignPx);   // 96 DPI 设计基线（含 macOS 标题区）

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int row = 0;
        void AddRow(string label, Control control)
        {
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = UiTheme.Text, Margin = new Padding(3, 8, 10, 3) }, 0, row);
            table.Controls.Add(control, 1, row);
            row++;
        }

        var tip = new Label
        {
            Text = "请切换到你想要获取窗口信息的窗口（自动实时读取；显示自己界面时保留上一次信息）",
            AutoSize = true,
            ForeColor = UiTheme.Dim,
            Margin = new Padding(3, 3, 3, 8),
        };
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(tip, 0, row);
        table.SetColumnSpan(tip, 2);
        row++;

        AddRow("窗口进程", _process);
        AddRow("窗口类名", _rootClass);
        AddRow("控件类名", _focusClass);
        AddRow("窗口标题", _title);
        AddRow("窗口进程路径", _path);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Margin = new Padding(3, 10, 3, 0),
        };
        _addButton.Click += (_, _) => AddRuleFromCurrent();
        var copy = UiTheme.Button("复制进程名(&C)", width: 130);
        copy.Click += (_, _) => CopyProcessName();
        var close = UiTheme.Button("关闭(&X)", width: 96);
        close.Click += (_, _) => Close();
        buttons.Controls.Add(_addButton);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(close);

        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(buttons, 0, row);
        table.SetColumnSpan(buttons, 2);
        row++;

        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(_status, 0, row);
        table.SetColumnSpan(_status, 2);

        Controls.Add(table);

        _timer.Tick += (_, _) => RefreshInfo();
        Load += (_, _) => PositionBottomRight();
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
        PerformLayout();   // 此时子树已齐备，AutoScale 按（当前 DPI / 96）整体缩放
    }

    private static TextBox MakeBox() => new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        BackColor = UiTheme.CardBg,
        Margin = new Padding(3, 4, 3, 2),
    };

    private void PositionBottomRight()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        int margin = UiTheme.Dpi(this, 24);
        Location = new Point(
            Math.Max(area.Left, area.Right - Width - margin),
            Math.Max(area.Top, area.Bottom - Height - margin));
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
            return; // 自身界面：保留上次内容

        WindowProbeResult info = WindowProbe.GetInfo(fg);
        _process.Text = info.Process;
        _rootClass.Text = info.RootClass;
        _focusClass.Text = info.FocusClass;
        _title.Text = info.Title;
        _path.Text = info.Path;

        if (info.Process.Length > 0)
        {
            _lastProcess = info.Process;
            _addButton.Enabled = true;
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
                _status.Text = $"已存在进程规则「{process}」，未重复添加。";
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

        _status.Text = $"已添加白名单规则：「{process}」（可在托盘菜单「白名单规则…」中调整或删除）";
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
}
