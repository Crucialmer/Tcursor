using System.Drawing.Drawing2D;
using Tcursor.Config;
using Tcursor.Render;

namespace Tcursor.Ui;

/// <summary>
/// 光标符号设置（仅矩形形状）。布局仿照 macOS：浅灰窗口 + 白色圆角卡片；
/// 左侧状态列表 → 右侧实时预览与样式编辑；保存时生成新样式实例（控制器按引用判定重绘）。
/// </summary>
internal sealed class SettingsForm : MacDialogForm
{
    private static readonly (string Key, string Title)[] States =
    {
        ("Chinese", "中文 CN"),
        ("English", "英文 EN"),
        ("CapsLock", "大写 Caps"),
        ("EnglishKeyboard", "英文键盘 US"),
        ("Japanese", "日文 JP"),
        ("Korean", "韩文 KR"),
    };

    private static readonly (string Text, int Value)[] CornerOptions =
    {
        ("直角", 0), ("小圆角", 1), ("中圆角", 2), ("大圆角（默认）", 3), ("特大圆角", 4), ("超大圆角", 5), ("胶囊", 6),
    };

    private static readonly (string Text, int Value)[] EdgeOptions =
    {
        ("无（默认）", 0), ("细描边", 1), ("内高光", 2), ("微立体", 3), ("柔和阴影", 4),
    };

    private readonly AppConfig _config;
    private readonly Dictionary<string, SymbolStyle> _edit;

    // 顶部
    private readonly MacSwitch _enabled = new() { Text = "启用光标符号" };
    private readonly MacRadio _below = new() { Text = "显示在光标下方", Checked = true };
    private readonly MacRadio _above = new() { Text = "显示在光标上方" };

    // 状态列表 / 明细
    private readonly ListBox _stateList = new();
    private readonly PreviewBox _preview = new();
    private readonly MacCheck _stateOff = new() { Text = "此状态不显示", BackColor = UiTheme.CardBg };
    private readonly MacButton _colorButton = new() { Width = 96, Height = 26, BackColor = UiTheme.CardBg };   // 位于卡片内：底色随卡片，避免衬底色差
    private readonly NumericUpDown _shapeWidth = new() { Minimum = 2, Maximum = 500, Width = 62 };
    private readonly NumericUpDown _shapeHeight = new() { Minimum = 2, Maximum = 500, Width = 62 };
    private readonly MacSlider _transparency = new() { Minimum = 0, Maximum = 255, Value = 255, Width = 150, BackColor = UiTheme.CardBg };
    private readonly Label _transparencyLabel = new() { AutoSize = true, Width = 32, ForeColor = UiTheme.Text, BackColor = UiTheme.CardBg };

    // 全局外观
    private readonly MacRadio[] _cornerButtons;
    private readonly MacRadio[] _edgeButtons;

    // 高级
    private readonly NumericUpDown _idleStop = new() { Minimum = 0, Maximum = 10000, Increment = 100, Width = 78 };
    private readonly NumericUpDown _hideDelay = new() { Minimum = 0, Maximum = 10000, Increment = 100, Width = 78 };
    private readonly NumericUpDown _pollInterval = new() { Minimum = 5, Maximum = 200, Increment = 5, Width = 64 };
    private readonly TextBox _chain = new() { Width = 150 };
    private readonly NumericUpDown _offsetX = new() { Minimum = -500, Maximum = 500, Width = 58 };
    private readonly NumericUpDown _offsetY = new() { Minimum = -500, Maximum = 500, Width = 58 };
    private readonly MacSwitch _logEnabled = new() { Text = "写入日志文件（排障用）", BackColor = UiTheme.CardBg };
    private readonly MacSwitch _updateAutoCheck = new() { Text = "启动时自动检查更新（每日一次）", BackColor = UiTheme.CardBg };
    private readonly MacSwitch _updateAutoDownload = new() { Text = "自动下载更新并在退出时安装（不打断使用）", BackColor = UiTheme.CardBg };

    private bool _loading;
    private string _currentKey = "Chinese";

    public SettingsForm(AppConfig config)
    {
        SuspendLayout();   // 设计器惯例：构造期间挂起布局，末段 ResumeLayout+PerformLayout 时统一触发 AutoScale 落地
        _config = config;

        _edit = new Dictionary<string, SymbolStyle>();
        foreach (var (key, style) in config.Styles)
            _edit[key] = style.Clone();
        Dictionary<string, SymbolStyle> defaults = AppConfig.CreateDefaultStyles();
        foreach (var (key, _) in States)
        {
            if (!_edit.ContainsKey(key))
                _edit[key] = defaults[key].Clone();
        }

        Text = "光标符号设置 - Tcursor";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(944, 724 + MacDialogForm.HeaderDesignPx);   // 圆角增至 7 档（两行）+ 提示换行后整体加高（96 DPI 设计基线；含 macOS 标题区）
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);   // 设计基线 96 DPI：缺失该值会使 AutoScaleFactor 恒为 1、高 DPI 完全不缩放
        MinimizeBox = false;
        MaximizeBox = false;

        // ---------- 顶部通用 ----------
        var topBar = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = UiTheme.WindowBg,
            Margin = new Padding(0, 0, 0, 2),
        };
        topBar.Controls.Add(_enabled);
        var posLabel = new Label
        {
            Text = "显示位置：",
            AutoSize = true,
            ForeColor = UiTheme.Dim,
            Margin = new Padding(28, 6, 2, 0),
            BackColor = UiTheme.WindowBg,
        };
        topBar.Controls.Add(posLabel);
        topBar.Controls.Add(_above);
        topBar.Controls.Add(_below);

        // ---------- 状态卡片 ----------
        foreach (var (_, title) in States)
            _stateList.Items.Add(title);
        _stateList.Dock = DockStyle.Fill;
        _stateList.BorderStyle = BorderStyle.None;
        _stateList.BackColor = UiTheme.CardBg;
        _stateList.DrawMode = DrawMode.OwnerDrawFixed;
        _stateList.ItemHeight = 34;   // 设计基线值；实际按当前 DPI 由 ApplyDpiMetrics 覆盖
        _stateList.Font = UiTheme.Font;
        _stateList.DrawItem += OnDrawStateItem;
        _stateList.SelectedIndex = 0;
        var stateCard = new MacCard("状态") { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 10, 0) };
        stateCard.Controls.Add(_stateList);

        // ---------- 预览卡片 ----------
        _preview.Dock = DockStyle.Fill;
        var previewCard = new MacCard("预览") { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        previewCard.Controls.Add(_preview);

        // ---------- 外观卡片（圆角 + 边框样式） ----------
        // 圆角 7 档分两行（4+3）：单行总宽超出卡片会被裁切
        _cornerButtons = new MacRadio[CornerOptions.Length];
        var cornerRow1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        var cornerRow2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        for (int i = 0; i < CornerOptions.Length; i++)
        {
            var radio = new MacRadio { Text = CornerOptions[i].Text, BackColor = UiTheme.CardBg, Margin = new Padding(2, 2, 14, 2) };
            _cornerButtons[i] = radio;
            (i < 4 ? cornerRow1 : cornerRow2).Controls.Add(radio);
        }
        var cornerRows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        cornerRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));   // 显式填充列，避免缺省 AutoSize 列被内容撑宽
        cornerRows.Controls.Add(cornerRow1, 0, 0);
        cornerRows.Controls.Add(cornerRow2, 0, 1);
        _edgeButtons = BuildRadioGroup(EdgeOptions, out FlowLayoutPanel edgeFlow);
        // BackColor 必须显式对齐卡片：缺省时继承窗口底色，提示文字（MaximumSize 限宽）右侧会露出一条窗口色窄带
        var lookRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        lookRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        lookRoot.Controls.Add(MakeRow("圆角", cornerRows), 0, 0);
        lookRoot.Controls.Add(MakeRow("边框样式", edgeFlow), 0, 1);
        var offsetFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        offsetFlow.Controls.Add(_offsetX);
        offsetFlow.Controls.Add(_offsetY);
        offsetFlow.Controls.Add(new Label { Text = "X / Y（所有状态共用）", AutoSize = true, ForeColor = UiTheme.Dim, Margin = new Padding(10, 6, 0, 0), BackColor = UiTheme.CardBg });
        lookRoot.Controls.Add(MakeRow("偏移", offsetFlow), 0, 2);
        var lookHint = new Label
        {
            Text = "圆角为 Apple 连续曲率：小 16% · 中 25% · 大 33%（默认）· 特大 40% · 超大 45% · 胶囊 50%；边框样式参照 macOS 控件",
            AutoSize = true,
            MaximumSize = new Size(650, 0),   // 限宽换行（长文案单行会溢出卡片）；值随 DPI 由框架缩放
            ForeColor = UiTheme.Dim,
            Margin = new Padding(2, 2, 0, 0),
            BackColor = UiTheme.CardBg,
        };
        lookRoot.Controls.Add(lookHint, 0, 3);
        var lookCard = new MacCard("外观") { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        lookCard.Controls.Add(lookRoot);

        // ---------- 样式卡片（当前状态） ----------
        _colorButton.Click += (_, _) => PickColor();
        var styleLine1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        styleLine1.Controls.Add(CellLabel("颜色"));
        styleLine1.Controls.Add(_colorButton);
        styleLine1.Controls.Add(CellLabel("宽"));
        styleLine1.Controls.Add(_shapeWidth);
        styleLine1.Controls.Add(CellLabel("高"));
        styleLine1.Controls.Add(_shapeHeight);
        styleLine1.Controls.Add(_stateOff);
        var styleLine2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        styleLine2.Controls.Add(CellLabel("透明度"));
        styleLine2.Controls.Add(_transparency);
        styleLine2.Controls.Add(_transparencyLabel);
        var styleRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        styleRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        styleRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        styleRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        styleRoot.Controls.Add(styleLine1, 0, 0);
        styleRoot.Controls.Add(styleLine2, 0, 1);
        var styleCard = new MacCard("样式（当前状态）") { Dock = DockStyle.Fill, AutoSize = true, MinimumSize = new Size(0, 120), Margin = new Padding(0, 8, 0, 0) };
        styleCard.Controls.Add(styleRoot);

        // ---------- 右侧列 ----------
        var rightCol = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0) };
        rightCol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rightCol.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        rightCol.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightCol.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightCol.Controls.Add(previewCard, 0, 0);
        rightCol.Controls.Add(lookCard, 0, 1);
        rightCol.Controls.Add(styleCard, 0, 2);

        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        middle.Controls.Add(stateCard, 0, 0);
        middle.Controls.Add(rightCol, 1, 0);

        // ---------- 高级卡片 ----------
        var advFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            MaximumSize = new Size(852, 0),   // 值随 DPI 由框架缩放
            Margin = new Padding(0),
            BackColor = UiTheme.CardBg,
        };
        advFlow.Controls.Add(CellLabel("空闲暂停(ms)"));
        advFlow.Controls.Add(_idleStop);
        advFlow.Controls.Add(CellLabel("空闲隐藏(0=不隐藏,ms)"));
        advFlow.Controls.Add(_hideDelay);
        advFlow.Controls.Add(CellLabel("轮询(ms)"));
        advFlow.Controls.Add(_pollInterval);
        advFlow.Controls.Add(CellLabel("默认捕获链"));
        advFlow.Controls.Add(_chain);
        advFlow.Controls.Add(_logEnabled);
        advFlow.Controls.Add(_updateAutoCheck);
        advFlow.Controls.Add(_updateAutoDownload);
        var advanced = new MacCard("高级") { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        advanced.Controls.Add(advFlow);

        // ---------- 底部按钮 ----------
        var reset = UiTheme.Button("恢复默认形状", width: 130);
        reset.Click += (_, _) => ResetDefaults();
        var ok = UiTheme.Button("确定", accent: true, width: 96);
        ok.DialogResult = DialogResult.OK;
        ok.Click += (_, _) => CollectAndApply();
        var cancel = UiTheme.Button("取消", width: 96);
        cancel.DialogResult = DialogResult.Cancel;

        var resetFlow = new FlowLayoutPanel { AutoSize = false, Width = 140, Height = 36, WrapContents = false, Dock = DockStyle.Left, BackColor = UiTheme.WindowBg, Margin = new Padding(0) };
        resetFlow.Controls.Add(reset);
        var rightFlow = new FlowLayoutPanel { AutoSize = false, Width = 210, Height = 36, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Right, BackColor = UiTheme.WindowBg, Margin = new Padding(0) };
        rightFlow.Controls.Add(ok);
        rightFlow.Controls.Add(cancel);
        var buttons = new Panel { Dock = DockStyle.Fill, Height = 46, Padding = new Padding(0, 10, 0, 0), BackColor = UiTheme.WindowBg };
        buttons.Controls.Add(rightFlow);
        buttons.Controls.Add(resetFlow);

        // ---------- 主布局 ----------
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12), BackColor = UiTheme.WindowBg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));   // 显式填充列：缺省 AutoSize 列会被子内容撑宽导致横向溢出
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(topBar, 0, 0);
        root.Controls.Add(middle, 0, 1);
        root.Controls.Add(advanced, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;

        // ---------- 事件 ----------
        for (int i = 0; i < _cornerButtons.Length; i++)
        {
            int idx = i;
            _cornerButtons[i].CheckedChanged += (_, _) =>
            {
                if (_loading || !_cornerButtons[idx].Checked)
                    return;
                // 圆角档位分两行（跨两个 FlowLayoutPanel），而 WinForms 单选互斥以父容器为界——
                // 分行后两行成为两个独立单选组，点第二行不会取消第一行 → 手动保证全组唯一选中
                for (int j = 0; j < _cornerButtons.Length; j++)
                {
                    if (j != idx && _cornerButtons[j].Checked)
                        _cornerButtons[j].Checked = false;
                }
                UpdatePreview();
            };
        }
        for (int i = 0; i < _edgeButtons.Length; i++)
        {
            int idx = i;
            _edgeButtons[i].CheckedChanged += (_, _) => { if (!_loading && _edgeButtons[idx].Checked) UpdatePreview(); };
        }
        _stateList.SelectedIndexChanged += (_, _) => OnStateChanged();
        _shapeWidth.ValueChanged += (_, _) => { if (!_loading) UpdatePreview(); };
        _shapeHeight.ValueChanged += (_, _) => { if (!_loading) UpdatePreview(); };
        _transparency.ValueChanged += (_, _) =>
        {
            _transparencyLabel.Text = _transparency.Value.ToString();
            if (!_loading) UpdatePreview();
        };
        _stateOff.CheckedChanged += (_, _) => { if (!_loading) { RefreshControlsEnabled(); UpdatePreview(); } };

        LoadValues();

        ResumeLayout(false);
        PerformLayout();   // 此时子树已齐备，AutoScale 按（当前 DPI / 96）整体缩放
        ApplyDpiMetrics();
    }

    /// <summary>AutoScale 不覆盖的像素级度量（状态列表行高）：按当前 DPI 应用。</summary>
    private void ApplyDpiMetrics()
    {
        int itemHeight = UiTheme.Dpi(_stateList, 34);
        if (_stateList.ItemHeight != itemHeight)
            _stateList.ItemHeight = itemHeight;
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
        _stateList.Invalidate();
    }

    // ---------------- 构建辅助 ----------------

    private static Label CellLabel(string text)
        => new() { Text = text, AutoSize = true, ForeColor = UiTheme.Text, Margin = new Padding(10, 6, 3, 0), BackColor = UiTheme.CardBg };

    private static Control MakeRow(string label, Control content)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Margin = new Padding(2, 6, 0, 0),
            BackColor = UiTheme.CardBg,
        }, 0, 0);
        row.Controls.Add(content, 1, 0);
        return row;
    }

    private static MacRadio[] BuildRadioGroup((string Text, int Value)[] options, out FlowLayoutPanel flow)
    {
        var radios = new MacRadio[options.Length];
        flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        for (int i = 0; i < options.Length; i++)
        {
            radios[i] = new MacRadio { Text = options[i].Text, BackColor = UiTheme.CardBg, Margin = new Padding(2, 2, 14, 2) };
            flow.Controls.Add(radios[i]);
        }
        return radios;
    }

    // ---------------- 状态列表绘制（macOS 侧栏风格） ----------------

    private void OnDrawStateItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
            return;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(UiTheme.CardBg))
            g.FillRectangle(bg, e.Bounds);

        float s = UiTheme.DpiScale(_stateList);
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var item = new RectangleF(e.Bounds.X + 4f * s, e.Bounds.Y + 3f * s, e.Bounds.Width - 8f * s, e.Bounds.Height - 6f * s);
        if (selected)
        {
            using var path = MacDraw.RoundedPath(item, 7f * s);
            using var brush = new SolidBrush(UiTheme.Accent);
            g.FillPath(brush, path);
        }

        int textX = e.Bounds.X + (int)Math.Round(14 * s);
        TextRenderer.DrawText(g, Convert.ToString(_stateList.Items[e.Index]) ?? "", _stateList.Font,
            new Rectangle(textX, e.Bounds.Y, e.Bounds.Width - (int)Math.Round(18 * s), e.Bounds.Height),
            selected ? Color.White : UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // ---------------- 取值/赋值 ----------------

    private void LoadValues()
    {
        _loading = true;
        try
        {
            _enabled.Checked = _config.Enabled;
            _below.Checked = _config.OriginBelow;
            _above.Checked = !_config.OriginBelow;
            SetRadio(_cornerButtons, CornerOptions, Math.Clamp(_config.ShapeCornerPreference, 0, 6));
            SetRadio(_edgeButtons, EdgeOptions, Math.Clamp(_config.ShapeEdgeStyle, 0, 4));
            _idleStop.Value = ClampN(_config.IdleStopMs, _idleStop);
            _hideDelay.Value = ClampN(_config.HideDelayMs, _hideDelay);
            _pollInterval.Value = ClampN(_config.PollIntervalMs, _pollInterval);
            _chain.Text = _config.DefaultCaptureChain;
            _offsetX.Value = ClampN(_config.ScreenOffsetX, _offsetX);
            _offsetY.Value = ClampN(_config.ScreenOffsetY, _offsetY);
            _logEnabled.Checked = _config.LogEnabled;
            _updateAutoCheck.Checked = _config.UpdateAutoCheck;
            _updateAutoDownload.Checked = _config.UpdateAutoDownload;
        }
        finally
        {
            _loading = false;
        }
        LoadCurrent();
        UpdatePreview();
    }

    private void OnStateChanged()
    {
        if (_loading)
            return;
        CommitCurrent();
        int index = Math.Max(0, _stateList.SelectedIndex);
        _currentKey = States[index].Key;
        LoadCurrent();
        UpdatePreview();
    }

    private void LoadCurrent()
    {
        _loading = true;
        try
        {
            SymbolStyle s = _edit[_currentKey];
            _stateOff.Checked = s.Kind == SymbolKind.None;
            Color c = SymbolRenderer.ParseColor(s.ShapeColor, Color.Red);
            _colorButton.Text = s.ShapeColor;
            _colorButton.Swatch = c;
            _shapeWidth.Value = Math.Clamp(s.ShapeWidth, 2, 500);
            _shapeHeight.Value = Math.Clamp(s.ShapeHeight, 2, 500);
            _transparency.Value = Math.Clamp(s.Transparency, 0, 255);
            _transparencyLabel.Text = _transparency.Value.ToString();
        }
        finally
        {
            _loading = false;
        }
        RefreshControlsEnabled();
    }

    private void CommitCurrent()
    {
        if (!_edit.TryGetValue(_currentKey, out SymbolStyle? s))
            return;
        s.Kind = _stateOff.Checked ? SymbolKind.None : SymbolKind.Shape;
        s.Shape = ShapeKind.Rect;
        s.ShapeColor = _colorButton.Text.Trim();
        s.ShapeWidth = (int)_shapeWidth.Value;
        s.ShapeHeight = (int)_shapeHeight.Value;
        s.Transparency = _transparency.Value;
    }

    private void CollectAndApply()
    {
        CommitCurrent();
        _config.Enabled = _enabled.Checked;
        _config.OriginBelow = _below.Checked;
        _config.ShapeCornerPreference = CurrentRadio(_cornerButtons, CornerOptions, 3);
        _config.ShapeEdgeStyle = CurrentRadio(_edgeButtons, EdgeOptions, 0);
        _config.IdleStopMs = (int)_idleStop.Value;
        _config.HideDelayMs = (int)_hideDelay.Value;
        _config.PollIntervalMs = (int)_pollInterval.Value;
        _config.DefaultCaptureChain = _chain.Text.Trim();
        _config.ScreenOffsetX = (int)_offsetX.Value;
        _config.ScreenOffsetY = (int)_offsetY.Value;
        _config.LogEnabled = _logEnabled.Checked;
        _config.UpdateAutoCheck = _updateAutoCheck.Checked;
        _config.UpdateAutoDownload = _updateAutoDownload.Checked;
        _config.StyleVersion = AppConfig.CurrentStyleVersion;   // 与 Save 回写一致；勿低于 Normalize 最新迁移段

        var styles = new Dictionary<string, SymbolStyle>();
        foreach (var (key, style) in _edit)
            styles[key] = style.Clone();   // 新实例：控制器按引用判定重绘
        _config.Styles = styles;
    }

    // ---------------- 预览与交互 ----------------

    private void UpdatePreview()
    {
        if (_loading)
            return;
        var temp = new SymbolStyle
        {
            Kind = _stateOff.Checked ? SymbolKind.None : SymbolKind.Shape,
            Shape = ShapeKind.Rect,
            ShapeColor = _colorButton.Text.Trim(),
            ShapeWidth = (int)_shapeWidth.Value,
            ShapeHeight = (int)_shapeHeight.Value,
            Transparency = _transparency.Value,
        };
        _preview.SetSymbol(temp, CurrentRadio(_cornerButtons, CornerOptions, 3), CurrentRadio(_edgeButtons, EdgeOptions, 0));
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { FullOpen = true, Color = _colorButton.Swatch ?? Color.Red };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _colorButton.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            _colorButton.Swatch = dialog.Color;
            UpdatePreview();
        }
    }

    private void ResetDefaults()
    {
        if (!MacDialog.Confirm(this, "恢复所有状态为默认形状（颜色/尺寸），并重置圆角/边框？", "恢复默认"))
            return;

        Dictionary<string, SymbolStyle> defaults = AppConfig.CreateDefaultStyles();
        foreach (var (key, _) in States)
            _edit[key] = defaults[key].Clone();

        _loading = true;
        try
        {
            SetRadio(_cornerButtons, CornerOptions, 3);   // 3 = 大圆角（默认档）
            SetRadio(_edgeButtons, EdgeOptions, 0);
        }
        finally
        {
            _loading = false;
        }

        LoadCurrent();
        UpdatePreview();
    }

    private void RefreshControlsEnabled()
    {
        bool on = !_stateOff.Checked;
        _colorButton.Enabled = on;
        _shapeWidth.Enabled = on;
        _shapeHeight.Enabled = on;
        _transparency.Enabled = on;
    }

    // ---------------- 小工具 ----------------

    private static void SetRadio(MacRadio[] radios, (string Text, int Value)[] options, int value)
    {
        for (int i = 0; i < radios.Length; i++)
            radios[i].Checked = options[i].Value == value;
    }

    private static int CurrentRadio(MacRadio[] radios, (string Text, int Value)[] options, int fallback)
    {
        for (int i = 0; i < radios.Length; i++)
        {
            if (radios[i].Checked)
                return options[i].Value;
        }
        return fallback;
    }

    private static decimal ClampN(int value, NumericUpDown input)
        => Math.Clamp(value, (int)input.Minimum, (int)input.Maximum);
}

/// <summary>实时预览盒：棋盘格背景 + 渲染位图（放大无插值，保持像素边缘真实观感）</summary>
internal sealed class PreviewBox : Control
{
    private Bitmap? _bitmap;
    private string _emptyText = "";

    // 棋盘格背景缓存（拖动滑杆/切档位会高频重绘，逐格 FillRectangle 是纯浪费）
    private Bitmap? _checker;
    private int _checkerCell;
    private Size _checkerSize;
    private UiPalette? _checkerPalette;

    public PreviewBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.CardBg;
    }

    public void SetSymbol(SymbolStyle style, int corner, int edge)
    {
        Bitmap? old = _bitmap;
        if (style.Kind != SymbolKind.Shape)
        {
            _bitmap = null;
            _emptyText = "此状态不显示";
        }
        else
        {
            double s = UiTheme.DpiScale(this);
            double maxSide = Math.Max(1, Math.Max(style.ShapeWidth, style.ShapeHeight));
            double scale = Math.Min(4.0 * s, UiTheme.Dpi(this, 152) / maxSide);
            if (scale < 0.2 * s)
                scale = 0.2 * s;
            _bitmap = SymbolRenderer.Render(style, corner, edge, scale);
            _emptyText = "";
        }
        old?.Dispose();
        Invalidate();
    }

    /// <summary>棋盘格背景位图按需重建（尺寸/格子边长/调色板任一变化时）</summary>
    private void EnsureChecker(int cell)
    {
        UiPalette palette = UiTheme.Current;
        if (_checker is not null && _checkerCell == cell && _checkerSize == ClientSize && ReferenceEquals(_checkerPalette, palette))
            return;

        _checker?.Dispose();
        int w = Math.Max(1, Width);
        int h = Math.Max(1, Height);
        _checker = new Bitmap(w, h);
        using (var gc = Graphics.FromImage(_checker))
        using (var even = new SolidBrush(palette.CheckerEven))
        using (var odd = new SolidBrush(palette.CheckerOdd))
        {
            for (int y = 0; y < h; y += cell)
            {
                for (int x = 0; x < w; x += cell)
                {
                    gc.FillRectangle(((x / cell + y / cell) % 2) == 0 ? even : odd, x, y, cell, cell);
                }
            }
        }
        _checkerCell = cell;
        _checkerSize = ClientSize;
        _checkerPalette = palette;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int cell = Math.Max(4, UiTheme.Dpi(this, 8));
        EnsureChecker(cell);
        if (_checker is not null)
            e.Graphics.DrawImageUnscaled(_checker, 0, 0);

        if (_bitmap is not null)
        {
            int px = (Width - _bitmap.Width) / 2;
            int py = (Height - _bitmap.Height) / 2;
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(_bitmap, new Rectangle(px, py, _bitmap.Width, _bitmap.Height));
        }
        else if (_emptyText.Length > 0)
        {
            TextRenderer.DrawText(e.Graphics, _emptyText, Font, ClientRectangle, UiTheme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _checker?.Dispose();
            _checker = null;
        }
        base.Dispose(disposing);
    }
}
