using Tcursor.Config;

namespace Tcursor.Ui;

/// <summary>白名单规则管理窗体（命中任一启用规则才显示符号；列表顺序即匹配顺序）</summary>
internal sealed class RulesForm : MacDialogForm
{
    /// <summary>列宽（96 DPI 设计基线；合计 630px —— 列表包卡片后可用内宽约 646px，留余量避免出现横向滚动条）</summary>
    private static readonly (string Title, int Width)[] Columns =
    {
        ("启用", 40), ("名称", 86), ("进程正则", 138), ("标题正则", 110), ("类名正则", 98), ("捕获链", 88), ("追加偏移", 70),
    };

    private readonly AppConfig _config;
    private readonly List<RuleConfig> _rules;
    private readonly ListView _list = new();

    public RulesForm(AppConfig config)
    {
        SuspendLayout();   // 设计器惯例：构造期间挂起布局，末段 ResumeLayout+PerformLayout 时统一触发 AutoScale 落地
        _config = config;
        _rules = config.Rules.Select(Clone).ToList();

        Text = "白名单规则 - Tcursor";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 440 + MacDialogForm.HeaderDesignPx);   // 96 DPI 设计基线（含 macOS 标题区）
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);   // 设计基线 96 DPI：缺失该值会使 AutoScaleFactor 恒为 1、高 DPI 完全不缩放
        MinimizeBox = false;
        MaximizeBox = false;

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.CheckBoxes = true;
        _list.Dock = DockStyle.Fill;
        _list.BorderStyle = BorderStyle.None;   // 卡片已提供边界，去掉列表自带边框
        _list.BackColor = UiTheme.CardBg;
        foreach (var (title, width) in Columns)
            _list.Columns.Add(title, width);   // 实际宽度按 DPI 由 ApplyDpiMetrics 覆盖
        _list.DoubleClick += (_, _) => EditSelected();
        // 勾选即回写模型：避免后续列表操作（增删/移动/编辑）重建列表时丢失未保存的勾选。
        // 只同步「触发事件的那一项」——窗体首次显示时 WinForms 会对每个已勾选项触发本事件，
        // 而此刻 Items 集合尾部尚未填充（全量枚举会产出 null 元素 → NullReferenceException）。
        _list.ItemChecked += (_, e) => SyncChecked(e.Item);

        var side = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
        };
        side.Controls.Add(MakeButton("添加…", (_, _) => AddRule()));
        side.Controls.Add(MakeButton("编辑…", (_, _) => EditSelected()));
        side.Controls.Add(MakeButton("删除", (_, _) => RemoveSelected()));
        side.Controls.Add(MakeButton("上移", (_, _) => MoveSelected(-1)));
        side.Controls.Add(MakeButton("下移", (_, _) => MoveSelected(1)));

        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(780, 0),   // 值随 DPI 由框架缩放
            ForeColor = UiTheme.Dim,
            Margin = new Padding(0, 0, 0, 2),
            Padding = new Padding(0, 6, 0, 6),
            Text = "命中任一启用规则才会显示光标符号（白名单机制）。\r\n" +
                   "进程正则匹配进程名（可不带 .exe）；标题/类名正则为可选的附加条件；列表自上而下按顺序匹配。\r\n" +
                   "提示：托盘菜单「窗口信息」可实时查看窗口进程，并一键添加规则。",
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
            Dock = DockStyle.Fill,
        };
        var ok = UiTheme.Button("确定", accent: true, width: 96);
        ok.DialogResult = DialogResult.OK;
        var cancel = UiTheme.Button("取消", width: 96);
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) =>
        {
            SyncAllChecked();
            _config.Rules = _rules;
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(14, 6, 14, 10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 列表包一层卡片：与设置窗口的卡片式观感统一（此前列表直接落在窗口底色上）
        var listCard = new MacCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), Padding = new Padding(10) };
        listCard.Controls.Add(_list);
        root.Controls.Add(hint, 0, 0);
        root.SetColumnSpan(hint, 2);
        root.Controls.Add(listCard, 0, 1);
        root.Controls.Add(side, 1, 1);
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);

        ReloadList();

        ResumeLayout(false);
        PerformLayout();   // 此时子树已齐备，AutoScale 按（当前 DPI / 96）整体缩放
        ApplyDpiMetrics();
    }

    /// <summary>AutoScale 不覆盖的像素级度量（ListView 列宽）：按当前 DPI 应用。</summary>
    private void ApplyDpiMetrics()
    {
        for (int i = 0; i < Columns.Length && i < _list.Columns.Count; i++)
            _list.Columns[i].Width = UiTheme.Dpi(_list, Columns[i].Width);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyDpiMetrics();   // 句柄创建后 DeviceDpi 为最终值
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpiMetrics();
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = UiTheme.Button(text, width: 100);
        button.Margin = new Padding(3);
        button.Click += onClick;
        return button;
    }

    private void ReloadList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (RuleConfig rule in _rules)
        {
            var item = new ListViewItem("") { Checked = rule.Enabled, Tag = rule };
            item.SubItems.Add(rule.Name);
            item.SubItems.Add(rule.Process);
            item.SubItems.Add(rule.Title);
            item.SubItems.Add(rule.Class);
            item.SubItems.Add(rule.CaptureChain);
            item.SubItems.Add($"{rule.OffsetX},{rule.OffsetY}");
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    private void AddRule()
    {
        using var dialog = new RuleEditForm(new RuleConfig { Name = "新规则" });
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null)
        {
            _rules.Add(dialog.Result);
            ReloadList();
        }
    }

    private void EditSelected()
    {
        if (_list.SelectedItems.Count == 0)
            return;
        var rule = (RuleConfig)_list.SelectedItems[0].Tag!;
        using var dialog = new RuleEditForm(Clone(rule));
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null)
        {
            int index = _rules.IndexOf(rule);
            if (index >= 0)
                _rules[index] = dialog.Result;
            ReloadList();
        }
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItems.Count == 0)
            return;
        var rule = (RuleConfig)_list.SelectedItems[0].Tag!;
        if (!MacDialog.Confirm(this, $"删除规则「{rule.Name}」？", "确认删除"))
            return;
        _rules.Remove(rule);
        ReloadList();
    }

    private void MoveSelected(int delta)
    {
        if (_list.SelectedItems.Count == 0)
            return;
        var rule = (RuleConfig)_list.SelectedItems[0].Tag!;
        int index = _rules.IndexOf(rule);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _rules.Count)
            return;
        (_rules[index], _rules[target]) = (_rules[target], _rules[index]);
        ReloadList();
        foreach (ListViewItem item in _list.Items)
        {
            if (ReferenceEquals(item.Tag, rule))
            {
                item.Selected = true;
                item.Focused = true;
                break;
            }
        }
    }

    private void SyncChecked(ListViewItem? item)
    {
        if (item?.Tag is RuleConfig rule)
            rule.Enabled = item.Checked;
    }

    /// <summary>提交前的兜底全量回写（空元素防御：集合构建期可能短暂产出 null 元素）。</summary>
    private void SyncAllChecked()
    {
        foreach (ListViewItem? item in _list.Items)
        {
            if (item?.Tag is RuleConfig rule)
                rule.Enabled = item.Checked;
        }
    }

    private static RuleConfig Clone(RuleConfig rule) => new()
    {
        Name = rule.Name,
        Process = rule.Process,
        Title = rule.Title,
        Class = rule.Class,
        Enabled = rule.Enabled,
        CaptureChain = rule.CaptureChain,
        OffsetX = rule.OffsetX,
        OffsetY = rule.OffsetY,
    };
}

/// <summary>单条规则编辑对话框</summary>
internal sealed class RuleEditForm : MacDialogForm
{
    public RuleConfig? Result { get; private set; }

    private readonly TextBox _name = new() { Dock = DockStyle.Fill };        // Dock=Fill：TLP 单元格内 AutoSize 文本框会被压到不可见宽度，须由单元格约束
    private readonly TextBox _process = new() { Dock = DockStyle.Fill };
    private readonly TextBox _title = new() { Dock = DockStyle.Fill };
    private readonly TextBox _class = new() { Dock = DockStyle.Fill };
    private readonly TextBox _chain = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _offsetX = new() { Minimum = -500, Maximum = 500, Width = 90 };
    private readonly NumericUpDown _offsetY = new() { Minimum = -500, Maximum = 500, Width = 90 };
    private readonly MacCheck _enabled = new() { Text = "启用", BackColor = UiTheme.WindowBg };

    public RuleEditForm(RuleConfig source)
    {
        SuspendLayout();   // 设计器惯例：同 SettingsForm，末段 ResumeLayout+PerformLayout 触发 AutoScale
        Text = "编辑规则";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 340 + MacDialogForm.HeaderDesignPx);   // 96 DPI 设计基线（含 macOS 标题区）
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);   // 设计基线 96 DPI（缺失则不缩放）
        MinimizeBox = false;
        MaximizeBox = false;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int row = 0;
        void AddRow(string label, Control control)
        {
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = UiTheme.Text, Margin = new Padding(3, 8, 3, 3) }, 0, row);
            table.Controls.Add(control, 1, row);
            row++;
        }

        AddRow("名称", _name);
        AddRow("进程正则（三选一至少一项）", _process);
        AddRow("标题正则（可选）", _title);
        AddRow("类名正则（可选）", _class);
        AddRow("捕获链（可选，如 GUI>UIA）", _chain);
        AddRow("追加偏移 X / Y", new FlowLayoutPanel { AutoSize = true, WrapContents = false, Controls = { _offsetX, _offsetY } });
        AddRow("", _enabled); // 左侧标签留空：复选框自带「启用」文字，避免重复

        _name.Text = source.Name;
        _process.Text = source.Process;
        _title.Text = source.Title;
        _class.Text = source.Class;
        _chain.Text = source.CaptureChain;
        _enabled.Checked = source.Enabled;
        _offsetX.Value = Math.Clamp(source.OffsetX, -500, 500);
        _offsetY.Value = Math.Clamp(source.OffsetY, -500, 500);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
            Dock = DockStyle.Fill,
        };
        var ok = UiTheme.Button("确定", accent: true, width: 96);
        var cancel = UiTheme.Button("取消", width: 96);
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) => Confirm();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));   // 显式填充列，避免缺省 AutoSize 列被内容撑宽
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(table, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        ResumeLayout(false);
        PerformLayout();
    }

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(_process.Text) &&
            string.IsNullOrWhiteSpace(_title.Text) &&
            string.IsNullOrWhiteSpace(_class.Text))
        {
            MacDialog.Alert(this, "进程 / 标题 / 类名 至少填写一项。");
            return;
        }

        Result = new RuleConfig
        {
            Name = _name.Text.Trim(),
            Process = _process.Text.Trim(),
            Title = _title.Text.Trim(),
            Class = _class.Text.Trim(),
            CaptureChain = _chain.Text.Trim(),
            Enabled = _enabled.Checked,
            OffsetX = (int)_offsetX.Value,
            OffsetY = (int)_offsetY.Value,
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
