using Tcursor.Config;

namespace Tcursor.Ui;

/// <summary>白名单规则管理窗体（命中任一启用规则才显示符号；列表顺序即匹配顺序；完美支持 8K 与高 DPI 缩放）</summary>
internal sealed class RulesForm : MacDialogForm
{
    private static readonly (string Title, int Width)[] Columns =
    {
        ("启用", 50), ("规则名称", 110), ("进程正则", 150), ("标题正则", 120), ("类名正则", 110), ("捕获链", 96), ("追加偏移", 80),
    };

    private readonly AppConfig _config;
    private readonly List<RuleConfig> _rules;
    private readonly ListView _list = new();
    private readonly ImageList _rowHeightSpacer = new();

    public RulesForm(AppConfig config)
    {
        SuspendLayout();
        _config = config;
        _rules = config.Rules.Select(Clone).ToList();

        Text = "白名单规则 - Tcursor";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(880, 480 + MacDialogForm.HeaderDesignPx);
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.CheckBoxes = true;
        _list.Dock = DockStyle.Fill;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = UiTheme.CardBg;
        _list.OwnerDraw = true;

        _list.DrawColumnHeader += (_, e) =>
        {
            Graphics g = e.Graphics;
            Rectangle bounds = e.Bounds;

            using (var brush = new SolidBrush(UiTheme.CardBg))
                g.FillRectangle(brush, bounds);

            using (var linePen = new Pen(UiTheme.CardBorder, 1f))
                g.DrawLine(linePen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);

            if (string.IsNullOrEmpty(e.Header?.Text))
                return;

            float s = UiTheme.DpiScale(_list);
            var textRect = new Rectangle(
                bounds.X + (int)Math.Round(4f * s), bounds.Y,
                Math.Max(0, bounds.Width - (int)Math.Round(8f * s)), bounds.Height);

            TextRenderer.DrawText(g, e.Header.Text, UiTheme.FontCaptionBold, textRect, UiTheme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };

        _list.DrawItem += (_, e) =>
        {
            if (e.Item is null)
                e.DrawDefault = true;
            else
                DrawRow(e.Graphics, e.Item, e.Bounds);
        };

        _list.DrawSubItem += (_, e) =>
        {
            if (e.Item is null)
                e.DrawDefault = true;
            else
                DrawCell(e.Graphics, e.Item, e.ColumnIndex, e.Bounds);
        };

        foreach (var (title, width) in Columns)
            _list.Columns.Add(title, width);

        _list.DoubleClick += (_, _) => EditSelected();
        _list.ItemChecked += (_, e) => SyncChecked(e.Item);

        var side = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 0, 4),
            BackColor = UiTheme.WindowBg,
        };
        side.Controls.Add(MakeButton("添加…", (_, _) => AddRule()));
        side.Controls.Add(MakeButton("编辑…", (_, _) => EditSelected()));
        side.Controls.Add(MakeButton("删除", (_, _) => RemoveSelected()));
        side.Controls.Add(MakeButton("上移", (_, _) => MoveSelected(-1)));
        side.Controls.Add(MakeButton("下移", (_, _) => MoveSelected(1)));

        var hint = new Label
        {
            AutoSize = true,
            Font = UiTheme.FontCaption,
            MaximumSize = new Size(840, 0),
            ForeColor = UiTheme.Dim,
            Margin = new Padding(0, 0, 0, 8),
            Text = "白名单机制：命中任一启用规则才会显示光标符号，按自上而下顺序依次匹配。" + Environment.NewLine +
                   "提示：可在托盘菜单打开「窗口信息」实时查看当前前台窗口并一键添加入规则库。",
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(8, 8, 0, 8),
            Dock = DockStyle.Fill,
            BackColor = UiTheme.WindowBg,
        };
        var ok = UiTheme.Button("确定", accent: true, width: 104);
        ok.DialogResult = DialogResult.OK;
        var cancel = UiTheme.Button("取消", width: 104);
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

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16, 12, 16, 12), BackColor = UiTheme.WindowBg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var listCard = new MacCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), Padding = new Padding(6) };
        listCard.Controls.Add(_list);
        root.Controls.Add(hint, 0, 0);
        root.SetColumnSpan(hint, 2);
        root.Controls.Add(listCard, 0, 1);
        root.Controls.Add(side, 1, 1);
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);

        ReloadList();

        Load += (_, _) =>
        {
            UpdateRowHeight();
            ApplyDpiMetrics();
        };

        ResumeLayout(false);
        PerformLayout();
        ApplyDpiMetrics();
    }

    private void UpdateRowHeight()
    {
        int rowH = Math.Max(26, UiTheme.Dpi(_list, 28));
        _rowHeightSpacer.ImageSize = new Size(1, rowH);
        _list.SmallImageList = _rowHeightSpacer;
    }

    private void ApplyDpiMetrics()
    {
        for (int i = 0; i < Columns.Length && i < _list.Columns.Count; i++)
            _list.Columns[i].Width = UiTheme.Dpi(_list, Columns[i].Width);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateRowHeight();
        ApplyDpiMetrics();
        Invalidate(true);
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = UiTheme.Button(text, width: 104);
        button.Margin = new Padding(0, 0, 0, 8);
        button.Click += onClick;
        return button;
    }

    private void ReloadList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (RuleConfig rule in _rules)
        {
            var item = new ListViewItem(new[]
            {
                "",
                rule.Name,
                rule.Process,
                rule.Title,
                rule.Class,
                rule.CaptureChain,
                rule.OffsetX != 0 || rule.OffsetY != 0 ? $"{rule.OffsetX},{rule.OffsetY}" : "",
            })
            {
                Checked = rule.Enabled,
                Tag = rule,
            };
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

    private void SyncAllChecked()
    {
        foreach (ListViewItem? item in _list.Items)
        {
            if (item?.Tag is RuleConfig rule)
                rule.Enabled = item.Checked;
        }
    }

    private void DrawRow(Graphics g, ListViewItem item, Rectangle rowBounds)
    {
        bool selected = item.Selected;
        Color surface = selected ? UiTheme.Accent : _list.BackColor;
        using (var brush = new SolidBrush(surface))
            g.FillRectangle(brush, rowBounds);

        int x = rowBounds.X;
        for (int col = 0; col < _list.Columns.Count; col++)
        {
            int width = _list.Columns[col].Width;
            DrawCell(g, item, col, new Rectangle(x, rowBounds.Y, width, rowBounds.Height));
            x += width;
        }
    }

    private void DrawCell(Graphics g, ListViewItem item, int column, Rectangle cellBounds)
    {
        bool selected = item.Selected;
        Color surface = selected ? UiTheme.Accent : _list.BackColor;
        using (var brush = new SolidBrush(surface))
            g.FillRectangle(brush, cellBounds);

        float s = UiTheme.DpiScale(_list);
        if (column == 0)
        {
            float box = 16f * s;
            var rect = new RectangleF(cellBounds.X + (cellBounds.Width - box) / 2f, cellBounds.Y + (cellBounds.Height - box) / 2f, box, box);
            MacDraw.DrawCheckBox(g, rect, item.Checked, _list.Enabled, surface);
            return;
        }

        string text = column < item.SubItems.Count ? item.SubItems[column].Text : "";
        if (text.Length == 0)
            return;
        var bounds = new Rectangle(
            cellBounds.X + (int)Math.Round(4f * s), cellBounds.Y,
            Math.Max(0, cellBounds.Width - (int)Math.Round(8f * s)), cellBounds.Height);
        Font cellFont = (column >= 2 && column <= 5) ? UiTheme.FontMono : _list.Font;
        TextRenderer.DrawText(g, text, cellFont, bounds, selected ? Color.White : UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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

/// <summary>单条规则编辑对话框（macOS 26 风格卡片布局）</summary>
internal sealed class RuleEditForm : MacDialogForm
{
    public RuleConfig? Result { get; private set; }

    private readonly MacField _name = new() { Dock = DockStyle.Fill, BackColor = UiTheme.CardBg };
    private readonly MacField _process = new() { Dock = DockStyle.Fill, BackColor = UiTheme.CardBg };
    private readonly MacField _title = new() { Dock = DockStyle.Fill, BackColor = UiTheme.CardBg };
    private readonly MacField _class = new() { Dock = DockStyle.Fill, BackColor = UiTheme.CardBg };
    private readonly MacField _chain = new() { Dock = DockStyle.Fill, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _offsetX = new() { Minimum = -500, Maximum = 500, Width = 90, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _offsetY = new() { Minimum = -500, Maximum = 500, Width = 90, BackColor = UiTheme.CardBg };
    private readonly MacCheck _enabled = new() { Text = "启用此规则", BackColor = UiTheme.CardBg };

    public RuleEditForm(RuleConfig source)
    {
        SuspendLayout();
        Text = "编辑白名单规则 - Tcursor";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 380 + MacDialogForm.HeaderDesignPx);
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        var card = new MacCard("规则属性") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.CardBg };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
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

        _process.Inner.Font = UiTheme.FontMono;
        _title.Inner.Font = UiTheme.FontMono;
        _class.Inner.Font = UiTheme.FontMono;
        _chain.Inner.Font = UiTheme.FontMono;

        _name.Text = source.Name;
        _process.Text = source.Process;
        _title.Text = source.Title;
        _class.Text = source.Class;
        _chain.Text = source.CaptureChain;
        _offsetX.Value = source.OffsetX;
        _offsetY.Value = source.OffsetY;
        _enabled.Checked = source.Enabled;

        AddRow("规则名称", _name);
        AddRow("进程正则", _process);
        AddRow("标题正则", _title);
        AddRow("类名正则", _class);
        AddRow("捕获链", _chain);

        var offsetFlow = new FlowLayoutPanel { AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        offsetFlow.Controls.Add(new Label { Text = "X:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), ForeColor = UiTheme.Dim });
        offsetFlow.Controls.Add(_offsetX);
        offsetFlow.Controls.Add(new Label { Text = "Y:", AutoSize = true, Margin = new Padding(12, 6, 4, 0), ForeColor = UiTheme.Dim });
        offsetFlow.Controls.Add(_offsetY);
        AddRow("追加偏移", offsetFlow);
        AddRow("启用状态", _enabled);
        card.Controls.Add(table);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Dock = DockStyle.Fill,
            BackColor = UiTheme.WindowBg,
        };
        var ok = UiTheme.Button("确定", accent: true, width: 100);
        ok.Click += (_, _) =>
        {
            Result = new RuleConfig
            {
                Name = _name.Text.Trim(),
                Process = _process.Text.Trim(),
                Title = _title.Text.Trim(),
                Class = _class.Text.Trim(),
                CaptureChain = _chain.Text.Trim(),
                OffsetX = (int)_offsetX.Value,
                OffsetY = (int)_offsetY.Value,
                Enabled = _enabled.Checked,
            };
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = UiTheme.Button("取消", width: 100);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 12, 16, 14), BackColor = UiTheme.WindowBg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        Load += (_, _) =>
        {
            table.ColumnStyles[0].Width = UiTheme.Dpi(this, 100);
        };

        ResumeLayout(false);
        PerformLayout();
    }
}
