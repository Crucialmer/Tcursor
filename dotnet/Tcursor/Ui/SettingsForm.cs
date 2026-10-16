using System.Drawing.Drawing2D;
using Tcursor.Config;
using Tcursor.Render;

namespace Tcursor.Ui;

/// <summary>
/// 光标符号设置（macOS 26 风格 4 标签页架构）：
/// 状态与符号 / 外观与位置 / 行为与捕获 / 系统与更新。
/// 彻底解决拥挤杂乱，全面适配 8K 与高 DPI 极清缩放。
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
        ("直角 (0%)", 0), ("小圆角 (16%)", 1), ("中圆角 (25%)", 2), ("大圆角 (33%)", 3),
        ("特大圆角 (40%)", 4), ("超大圆角 (45%)", 5), ("胶囊 (50%)", 6),
    };

    private static readonly (string Text, int Value)[] EdgeOptions =
    {
        ("无 (默认)", 0), ("细描边", 1), ("内高光", 2), ("微立体", 3), ("柔和阴影", 4),
    };

    private readonly AppConfig _config;
    private readonly Dictionary<string, SymbolStyle> _edit;

    // 标签页选择器（将状态符号与外观位置合并，实时查看光标符号预览）
    private readonly MacSegmentedControl _segmented = new("🎨 符号与外观", "⚡ 行为与捕获", "⚙️ 系统与更新");
    private readonly Panel _contentContainer = new() { Dock = DockStyle.Fill, BackColor = UiTheme.WindowBg };
    private readonly Panel[] _panes = new Panel[3];

    // ----- Tab 0: 符号与外观 (合并状态、符号、圆角、光效与位置) -----
    private readonly ListBox _stateList = new();
    private readonly PreviewBox _preview = new();
    private readonly MacCheck _stateOff = new() { Text = "此状态不显示光标符号", BackColor = UiTheme.CardBg };
    private readonly MacButton _colorButton = new() { Width = 125, Height = 32, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _shapeWidth = new() { Minimum = 2, Maximum = 500, Width = 80 };
    private readonly MacNumberField _shapeHeight = new() { Minimum = 2, Maximum = 500, Width = 80 };
    private readonly MacSlider _transparency = new() { Minimum = 0, Maximum = 255, Value = 255, Width = 160, BackColor = UiTheme.CardBg };
    private readonly Label _transparencyLabel = new() { AutoSize = true, ForeColor = UiTheme.Text, BackColor = UiTheme.CardBg };

    // 各分页面板引用（便于 DPI 缩放与自适应布局维持行高，彻底防止挤压遮挡）
    private TableLayoutPanel? _symbolsLeftCol;
    private TableLayoutPanel? _symbolsRightCol;
    private TableLayoutPanel? _behaviorPanel;
    private TableLayoutPanel? _systemPanel;
    private MacCard? _edgeCard;
    private MacCard? _posCard;
    private MacCard? _lifeCard;
    private MacCard? _captureCard;
    private MacCard? _updateCard;
    private MacCard? _dataCard;

    // ----- 外观与位置控制项 -----
    private readonly MacRadio[] _cornerButtons;
    private readonly MacRadio[] _edgeButtons;
    private readonly MacRadio _below = new() { Text = "光标下方 (默认)", Checked = true, BackColor = UiTheme.CardBg };
    private readonly MacRadio _above = new() { Text = "光标上方", BackColor = UiTheme.CardBg };
    private readonly MacNumberField _offsetX = new() { Minimum = -500, Maximum = 500, Width = 80, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _offsetY = new() { Minimum = -500, Maximum = 500, Width = 80, BackColor = UiTheme.CardBg };

    // ----- Tab 2: 行为与捕获 -----
    private readonly MacSwitch _enabled = new() { Text = "启用光标符号 (主开关)", BackColor = UiTheme.CardBg };
    private readonly MacNumberField _idleStop = new() { Minimum = 0, Maximum = 10000, Increment = 100, Width = 110, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _hideDelay = new() { Minimum = 0, Maximum = 10000, Increment = 100, Width = 110, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _pollInterval = new() { Minimum = 5, Maximum = 200, Increment = 5, Width = 110, BackColor = UiTheme.CardBg };
    private readonly MacField _chain = new() { Width = 260, BackColor = UiTheme.CardBg };
    private readonly MacSwitch _tsfFallback = new() { Text = "TSF 光标读取 (微信、QQ、WPS 等封闭客户端回退支持)", BackColor = UiTheme.CardBg };

    // ----- Tab 3: 系统与更新 -----
    private readonly MacSwitch _updateAutoCheck = new() { Text = "启动时自动检查更新 (每日一次)", BackColor = UiTheme.CardBg };
    private readonly MacSwitch _updateAutoDownload = new() { Text = "自动下载并静默安装更新 (退出时或下次启动无感完成)", BackColor = UiTheme.CardBg };
    private readonly MacSwitch _logEnabled = new() { Text = "写入排障日志文件 (debug.log)", BackColor = UiTheme.CardBg };

    private bool _loading;
    private string _currentKey = "Chinese";

    public SettingsForm(AppConfig config)
    {
        SuspendLayout();
        _loading = true;
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
        ClientSize = new Size(960, 680 + MacDialogForm.HeaderDesignPx);
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        _cornerButtons = BuildRadioGroup(CornerOptions, out FlowLayoutPanel cornerFlow);
        _edgeButtons = BuildRadioGroup(EdgeOptions, out FlowLayoutPanel edgeFlow);

        // 顶层主网格
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16, 12, 16, 14),
            BackColor = UiTheme.WindowBg,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // Segmented Bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Tab Content
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // Bottom Buttons

        // 标签切换栏
        _segmented.Dock = DockStyle.Fill;
        _segmented.Margin = new Padding(0, 0, 0, 12);
        _segmented.SelectedIndexChanged += (_, _) => ShowPane(_segmented.SelectedIndex);
        root.Controls.Add(_segmented, 0, 0);

        // 初始化三大分页面板（符号与外观合并，实时预览全部样式与光效）
        _panes[0] = BuildSymbolsAndAppearancePane(cornerFlow, edgeFlow);
        _panes[1] = BuildBehaviorPane();
        _panes[2] = BuildSystemPane();

        foreach (var pane in _panes)
        {
            pane.Dock = DockStyle.Fill;
            pane.Visible = false;
            _contentContainer.Controls.Add(pane);
        }
        root.Controls.Add(_contentContainer, 0, 1);

        // 底部动作栏
        var bottomBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Height = 48,
            Margin = new Padding(0, 10, 0, 0),
            BackColor = UiTheme.WindowBg,
        };
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var leftButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };
        var reset = UiTheme.Button("恢复默认设置", width: 120);
        reset.Click += (_, _) => ResetDefaults();
        leftButtons.Controls.Add(reset);

        var rightButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };
        var ok = UiTheme.Button("保存并应用", accent: true, width: 120);
        ok.Click += (_, _) =>
        {
            CollectAndApply();
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = UiTheme.Button("取消", width: 100);
        cancel.DialogResult = DialogResult.Cancel;
        rightButtons.Controls.Add(ok);
        rightButtons.Controls.Add(cancel);

        bottomBar.Controls.Add(leftButtons, 0, 0);
        bottomBar.Controls.Add(rightButtons, 1, 0);
        root.Controls.Add(bottomBar, 0, 2);

        Controls.Add(root);

        // 事件绑定
        _colorButton.Click += (_, _) => PickColor();
        _shapeWidth.ValueChanged += (_, _) => { if (!_loading) { CommitCurrent(); UpdatePreview(); } };
        _shapeHeight.ValueChanged += (_, _) => { if (!_loading) { CommitCurrent(); UpdatePreview(); } };
        _transparency.ValueChanged += (_, _) =>
        {
            int pct = (int)Math.Round(_transparency.Value * 100.0 / 255.0);
            _transparencyLabel.Text = $"{pct}%";
            if (!_loading) { CommitCurrent(); UpdatePreview(); }
        };
        _stateOff.CheckedChanged += (_, _) =>
        {
            RefreshControlsEnabled();
            if (!_loading) { CommitCurrent(); UpdatePreview(); }
        };

        foreach (var radio in _cornerButtons)
            radio.CheckedChanged += (_, _) => { if (!_loading && radio.Checked) UpdatePreview(); };
        foreach (var radio in _edgeButtons)
            radio.CheckedChanged += (_, _) => { if (!_loading && radio.Checked) UpdatePreview(); };

        ShowPane(0);
        LoadValues();

        ResumeLayout(false);
        PerformLayout();
        ApplyDpiMetrics();
    }

    /// <summary>切换选中的设置标签页（0: 状态与符号, 1: 外观与位置, 2: 行为与捕获, 3: 系统与更新）</summary>
    public void SelectTab(int index)
    {
        int i = Math.Clamp(index, 0, _panes.Length - 1);
        _segmented.SelectedIndex = i;
        ShowPane(i);
    }

    private void ShowPane(int index)
    {
        for (int i = 0; i < _panes.Length; i++)
            _panes[i].Visible = i == index;
    }

    private Panel BuildSymbolsAndAppearancePane(FlowLayoutPanel cornerFlow, FlowLayoutPanel edgeFlow)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ========== 左列：输入法状态 + 光标位置与偏移 ==========
        var leftCol = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 0),
            BackColor = UiTheme.WindowBg,
        };
        _symbolsLeftCol = leftCol;
        leftCol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        leftCol.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Dpi(this, 300)));
        leftCol.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // 左上：输入法状态列表
        foreach (var (_, title) in States)
            _stateList.Items.Add(title);
        _stateList.Dock = DockStyle.Fill;
        _stateList.BorderStyle = BorderStyle.None;
        _stateList.BackColor = UiTheme.CardBg;
        _stateList.DrawMode = DrawMode.OwnerDrawFixed;
        _stateList.Font = UiTheme.Font;
        _stateList.DrawItem += OnDrawStateItem;
        _stateList.SelectedIndexChanged += (_, _) => OnStateChanged();
        _stateList.SelectedIndex = 0;

        var stateCard = new MacCard("输入法状态")
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
        };
        stateCard.Controls.Add(_stateList);
        leftCol.Controls.Add(stateCard, 0, 0);

        // 左下：光标相对位置与全局偏移微调
        _posCard = new MacCard("光标相对位置与全局偏移")
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        var posTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = UiTheme.CardBg,
        };
        posTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        posTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var posRadios = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            BackColor = UiTheme.CardBg,
            Margin = new Padding(0),
        };
        _below.Margin = new Padding(0, 2, 0, 2);
        _above.Margin = new Padding(0, 2, 0, 2);
        posRadios.Controls.Add(_below);
        posRadios.Controls.Add(_above);

        var offsetFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = UiTheme.CardBg,
            Margin = new Padding(0),
        };
        offsetFlow.Controls.Add(new Label { Text = "X:", AutoSize = true, Margin = new Padding(0, 5, 2, 0), ForeColor = UiTheme.Dim });
        _offsetX.Width = 52;
        offsetFlow.Controls.Add(_offsetX);
        offsetFlow.Controls.Add(new Label { Text = "Y:", AutoSize = true, Margin = new Padding(6, 5, 2, 0), ForeColor = UiTheme.Dim });
        _offsetY.Width = 52;
        offsetFlow.Controls.Add(_offsetY);

        posTable.RowCount = 4;
        posTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        posTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        posTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        posTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        posTable.Controls.Add(new Label { Text = "锚点位置", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 4, 4, 4), BackColor = UiTheme.CardBg }, 0, 0);
        posTable.Controls.Add(posRadios, 1, 0);
        posTable.Controls.Add(new Label { Text = "全局偏移", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 6, 4, 4), BackColor = UiTheme.CardBg }, 0, 1);
        posTable.Controls.Add(offsetFlow, 1, 1);

        var hintLabel = new Label
        {
            Text = "(像素偏移，全局所有状态共用)",
            AutoSize = true,
            Font = UiTheme.FontCaption,
            ForeColor = UiTheme.Dim,
            Margin = new Padding(0, 4, 0, 0),
            BackColor = UiTheme.CardBg,
        };
        posTable.Controls.Add(hintLabel, 1, 2);

        _posCard.Controls.Add(posTable);
        leftCol.Controls.Add(_posCard, 0, 1);

        panel.Controls.Add(leftCol, 0, 0);

        // ========== 右列：实时预览 + 状态样式配置 + 边框光效与全局圆角 ==========
        var rightCol = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };
        _symbolsRightCol = rightCol;
        rightCol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rightCol.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Dpi(this, 160)));
        rightCol.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Dpi(this, 195)));
        rightCol.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // 右上：实时视觉预览
        _preview.Dock = DockStyle.Fill;
        var previewCard = new MacCard("实时视觉预览")
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
        };
        previewCard.Controls.Add(_preview);
        rightCol.Controls.Add(previewCard, 0, 0);

        // 右中：当前状态样式配置
        var styleCard = new MacCard("当前状态样式配置")
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
        };
        var styleTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = UiTheme.CardBg,
        };
        styleTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        styleTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int row = 0;
        void AddStyleRow(string label, Control control)
        {
            styleTable.RowCount = row + 1;
            styleTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            styleTable.Controls.Add(new Label { Text = label, AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 6, 6, 2), BackColor = UiTheme.CardBg }, 0, row);
            styleTable.Controls.Add(control, 1, row);
            row++;
        }

        AddStyleRow("显示开关", _stateOff);

        var colorFlow = new FlowLayoutPanel { AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        colorFlow.Controls.Add(_colorButton);
        AddStyleRow("符号颜色", colorFlow);

        var sizeFlow = new FlowLayoutPanel { AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        sizeFlow.Controls.Add(new Label { Text = "宽:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), ForeColor = UiTheme.Dim });
        sizeFlow.Controls.Add(_shapeWidth);
        sizeFlow.Controls.Add(new Label { Text = "高:", AutoSize = true, Margin = new Padding(14, 6, 4, 0), ForeColor = UiTheme.Dim });
        sizeFlow.Controls.Add(_shapeHeight);
        AddStyleRow("符号尺寸", sizeFlow);

        var transFlow = new FlowLayoutPanel { AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        transFlow.Controls.Add(_transparency);
        _transparencyLabel.Margin = new Padding(8, 6, 0, 0);
        transFlow.Controls.Add(_transparencyLabel);
        AddStyleRow("不透明度", transFlow);

        styleCard.Controls.Add(styleTable);
        rightCol.Controls.Add(styleCard, 0, 1);

        // 右下：边框光效与全局圆角（macOS 原生效果）
        _edgeCard = new MacCard("边框光效与全局圆角 (macOS 原生效果)")
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        var edgeTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = UiTheme.CardBg,
        };
        edgeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        edgeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        edgeTable.RowCount = 2;
        edgeTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        edgeTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        edgeTable.Controls.Add(new Label { Text = "立体光效", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 4, 4, 2), BackColor = UiTheme.CardBg }, 0, 0);
        edgeTable.Controls.Add(edgeFlow, 1, 0);
        edgeTable.Controls.Add(new Label { Text = "全局圆角", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 4, 4, 2), BackColor = UiTheme.CardBg }, 0, 1);
        edgeTable.Controls.Add(cornerFlow, 1, 1);

        _edgeCard.Controls.Add(edgeTable);
        rightCol.Controls.Add(_edgeCard, 0, 2);

        panel.Controls.Add(rightCol, 1, 0);
        return panel;
    }

    private Panel BuildBehaviorPane()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };
        _behaviorPanel = panel;
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Dpi(this, 215)));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Dpi(this, 140)));

        _lifeCard = new MacCard("核心显示时机与生命周期")
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
        };
        var lifeTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.CardBg };
        lifeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        lifeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int r = 0;
        void AddLifeRow(string label, Control control)
        {
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                BackColor = UiTheme.CardBg,
                Margin = new Padding(0),
            };
            flow.Controls.Add(control);

            lifeTable.RowCount = r + 1;
            lifeTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lifeTable.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Font = UiTheme.FontBold,
                ForeColor = UiTheme.Text,
                Margin = new Padding(0, 8, 8, 4),
                BackColor = UiTheme.CardBg
            }, 0, r);
            lifeTable.Controls.Add(flow, 1, r);
            r++;
        }

        AddLifeRow("主运行状态", _enabled);
        AddLifeRow("空闲暂停(ms)", _idleStop);
        AddLifeRow("隐藏延时(ms)", _hideDelay);
        AddLifeRow("轮询周期(ms)", _pollInterval);
        _lifeCard.Controls.Add(lifeTable);
        panel.Controls.Add(_lifeCard, 0, 0);

        _captureCard = new MacCard("物理光标捕获与回退引擎")
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        var capTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.CardBg };
        capTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        capTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        capTable.RowCount = 2;
        capTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        capTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        capTable.Controls.Add(new Label { Text = "默认捕获链", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, 0);

        var chainFlow = new FlowLayoutPanel { AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        chainFlow.Controls.Add(_chain);
        capTable.Controls.Add(chainFlow, 1, 0);

        capTable.Controls.Add(new Label { Text = "封闭应用回退", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, 1);

        var tsfFlow = new FlowLayoutPanel { AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        tsfFlow.Controls.Add(_tsfFallback);
        capTable.Controls.Add(tsfFlow, 1, 1);

        _captureCard.Controls.Add(capTable);
        panel.Controls.Add(_captureCard, 0, 1);

        return panel;
    }

    private Panel BuildSystemPane()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };
        _systemPanel = panel;
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Dpi(this, 120)));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Dpi(this, 135)));

        _updateCard = new MacCard("软件自动更新")
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
        };
        var updateBox = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        updateBox.Controls.Add(_updateAutoCheck);
        updateBox.Controls.Add(_updateAutoDownload);
        _updateCard.Controls.Add(updateBox);
        panel.Controls.Add(_updateCard, 0, 0);

        _dataCard = new MacCard("排障与数据存储")
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        var dataTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.CardBg };
        dataTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        dataTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var dirButtons = new FlowLayoutPanel { AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        var openCfg = UiTheme.Button("打开配置文件", width: 120);
        openCfg.Click += (_, _) => ShellOpen(ConfigStore.ConfigPath);
        var openDir = UiTheme.Button("打开数据目录", width: 120);
        openDir.Click += (_, _) => ShellOpen(ConfigStore.DataDir);
        dirButtons.Controls.Add(openCfg);
        dirButtons.Controls.Add(openDir);

        dataTable.RowCount = 2;
        dataTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dataTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dataTable.Controls.Add(new Label { Text = "排障日志", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, 0);
        dataTable.Controls.Add(_logEnabled, 1, 0);
        dataTable.Controls.Add(new Label { Text = "文件目录", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, 1);
        dataTable.Controls.Add(dirButtons, 1, 1);
        _dataCard.Controls.Add(dataTable);
        panel.Controls.Add(_dataCard, 0, 1);

        return panel;
    }

    private static void ShellOpen(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    private static MacRadio[] BuildRadioGroup((string Text, int Value)[] options, out FlowLayoutPanel flow)
    {
        var radios = new MacRadio[options.Length];
        flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        for (int i = 0; i < options.Length; i++)
        {
            radios[i] = new MacRadio { Text = options[i].Text, BackColor = UiTheme.CardBg, Margin = new Padding(0, 3, 10, 3) };
            flow.Controls.Add(radios[i]);
        }
        return radios;
    }

    private void ApplyDpiMetrics()
    {
        int itemHeight = UiTheme.Dpi(_stateList, 36);
        if (_stateList.ItemHeight != itemHeight)
            _stateList.ItemHeight = itemHeight;

        if (_symbolsLeftCol is not null && _symbolsLeftCol.RowStyles.Count >= 2)
        {
            _symbolsLeftCol.RowStyles[0].Height = UiTheme.Dpi(this, 300);
        }
        if (_symbolsRightCol is not null && _symbolsRightCol.RowStyles.Count >= 3)
        {
            _symbolsRightCol.RowStyles[0].Height = UiTheme.Dpi(this, 160);
            _symbolsRightCol.RowStyles[1].Height = UiTheme.Dpi(this, 195);
        }
        if (_behaviorPanel is not null && _behaviorPanel.RowStyles.Count >= 2)
        {
            _behaviorPanel.RowStyles[0].Height = UiTheme.Dpi(this, 215);
            _behaviorPanel.RowStyles[1].Height = UiTheme.Dpi(this, 140);
        }
        if (_systemPanel is not null && _systemPanel.RowStyles.Count >= 2)
        {
            _systemPanel.RowStyles[0].Height = UiTheme.Dpi(this, 120);
            _systemPanel.RowStyles[1].Height = UiTheme.Dpi(this, 135);
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyDpiMetrics();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpiMetrics();
        _stateList.Invalidate();
        UpdatePreview();
    }

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
        var itemRect = new RectangleF(e.Bounds.X + 4f * s, e.Bounds.Y + 3f * s, e.Bounds.Width - 8f * s, e.Bounds.Height - 6f * s);
        if (selected)
        {
            using var path = MacDraw.RoundedPath(itemRect, 8f * s);
            using var brush = new SolidBrush(UiTheme.Accent);
            g.FillPath(brush, path);
        }

        // 绘制该状态当前颜色圆形 mini-badge
        string key = States[e.Index].Key;
        if (_edit.TryGetValue(key, out SymbolStyle? style))
        {
            Color dotColor = style.Kind == SymbolKind.None ? UiTheme.Dim : SymbolRenderer.ParseColor(style.ShapeColor, Color.Red);
            float dotD = 12f * s;
            float dotX = itemRect.X + 10f * s;
            float dotY = itemRect.Y + (itemRect.Height - dotD) / 2f;
            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, dotX, dotY, dotD, dotD);
            using var dotPen = new Pen(Color.FromArgb(50, 0, 0, 0), 1f);
            g.DrawEllipse(dotPen, dotX, dotY, dotD, dotD);
        }

        int textX = (int)Math.Round(itemRect.X + 30f * s);
        TextRenderer.DrawText(g, States[e.Index].Title, UiTheme.FontBold,
            new Rectangle(textX, e.Bounds.Y, e.Bounds.Width - textX - (int)(10 * s), e.Bounds.Height),
            selected ? Color.White : UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

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
            _tsfFallback.Checked = _config.TsfFallback;
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
        _stateList.Invalidate();
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
            int pct = (int)Math.Round(_transparency.Value * 100.0 / 255.0);
            _transparencyLabel.Text = $"{pct}%";
        }
        finally
        {
            _loading = false;
        }
        RefreshControlsEnabled();
    }

    private void CommitCurrent()
    {
        if (_loading || !_edit.TryGetValue(_currentKey, out SymbolStyle? s))
            return;
        s.Kind = _stateOff.Checked ? SymbolKind.None : SymbolKind.Shape;
        s.Shape = ShapeKind.Rect;
        if (!string.IsNullOrWhiteSpace(_colorButton.Text))
            s.ShapeColor = _colorButton.Text.Trim();
        s.ShapeWidth = (int)_shapeWidth.Value;
        s.ShapeHeight = (int)_shapeHeight.Value;
        s.Transparency = _transparency.Value;
        s.CornerPreference = -1; // 全局统一圆角
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
        _config.TsfFallback = _tsfFallback.Checked;
        _config.StyleVersion = AppConfig.CurrentStyleVersion;

        var styles = new Dictionary<string, SymbolStyle>();
        foreach (var (key, style) in _edit)
        {
            var clone = style.Clone();
            clone.CornerPreference = -1; // 统一遵从全局圆角设置
            styles[key] = clone;
        }
        _config.Styles = styles;
    }

    private void UpdatePreview()
    {
        if (_loading)
            return;
        int globalCorner = CurrentRadio(_cornerButtons, CornerOptions, 3);
        int globalEdge = CurrentRadio(_edgeButtons, EdgeOptions, 0);
        var temp = new SymbolStyle
        {
            Kind = _stateOff.Checked ? SymbolKind.None : SymbolKind.Shape,
            Shape = ShapeKind.Rect,
            ShapeColor = _colorButton.Text.Trim(),
            ShapeWidth = (int)_shapeWidth.Value,
            ShapeHeight = (int)_shapeHeight.Value,
            Transparency = _transparency.Value,
            CornerPreference = -1,
        };
        _preview.SetSymbol(temp, globalCorner, globalEdge);
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { FullOpen = true, Color = _colorButton.Swatch ?? Color.Red };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _colorButton.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            _colorButton.Swatch = dialog.Color;
            CommitCurrent();
            UpdatePreview();
            _stateList.Invalidate();
        }
    }

    private void ResetDefaults()
    {
        if (!MacDialog.Confirm(this, "恢复所有状态为默认形状（颜色/尺寸），并重置圆角与边框样式？", "恢复默认设置"))
            return;

        Dictionary<string, SymbolStyle> defaults = AppConfig.CreateDefaultStyles();
        foreach (var (key, _) in States)
            _edit[key] = defaults[key].Clone();

        _loading = true;
        try
        {
            SetRadio(_cornerButtons, CornerOptions, 3);
            SetRadio(_edgeButtons, EdgeOptions, 0);
        }
        finally
        {
            _loading = false;
        }

        LoadCurrent();
        UpdatePreview();
        _stateList.Invalidate();
    }

    private void RefreshControlsEnabled()
    {
        bool on = !_stateOff.Checked;
        _colorButton.Enabled = on;
        _shapeWidth.Enabled = on;
        _shapeHeight.Enabled = on;
        _transparency.Enabled = on;
    }

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

    private static decimal ClampN(int value, MacNumberField input)
        => Math.Clamp(value, (int)input.Minimum, (int)input.Maximum);
}

/// <summary>实时预览盒：高精度棋盘格背景 + 渲染位图（自适应舒适边距与居中，保持像素边缘真实观感，杜绝裁剪）</summary>
internal sealed class PreviewBox : Control
{
    private Bitmap? _bitmap;
    private string _emptyText = "";

    private Bitmap? _checker;
    private int _checkerCell;
    private Size _checkerSize;
    private UiPalette? _checkerPalette;

    private SymbolStyle? _lastStyle;
    private int _lastCorner;
    private int _lastEdge;

    public PreviewBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.CardBg;
    }

    public void SetSymbol(SymbolStyle style, int corner, int edge)
    {
        _lastStyle = style.Clone();
        _lastCorner = corner;
        _lastEdge = edge;
        RebuildBitmap();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _checker?.Dispose();
        _checker = null;
        RebuildBitmap();
    }

    private void RebuildBitmap()
    {
        if (_lastStyle is null || _lastStyle.Kind != SymbolKind.Shape)
        {
            Bitmap? old = _bitmap;
            _bitmap = null;
            _emptyText = _lastStyle is null ? "" : "此状态不显示光标符号";
            old?.Dispose();
            Invalidate();
            return;
        }

        double s = UiTheme.DpiScale(this);
        int availW = Math.Max(30, Width);
        int availH = Math.Max(30, Height);

        // 视口内部预留舒适呼吸留白（纵向至少留 24px，横向留 32px），防止光标符号与阴影边缘碰边裁剪
        double targetH = Math.Max(16.0, availH - 24.0 * s);
        double targetW = Math.Max(16.0, availW - 32.0 * s);

        double baseW = Math.Max(1, _lastStyle.ShapeWidth);
        double baseH = Math.Max(1, _lastStyle.ShapeHeight);

        // 「柔和阴影」样式自带扩散外扩 padding，计算安全系数以确保阴影完全舒展
        double shadowFactor = (_lastEdge == 4) ? 0.32 : 0.0;
        double shadowConst = (_lastEdge == 4) ? 6.0 * s : 0.0;

        double effH = baseH * (1.0 + shadowFactor) + shadowConst;
        double effW = baseW * (1.0 + shadowFactor) + shadowConst;

        double fitScale = Math.Min(targetW / effW, targetH / effH);

        // 缩放比例限制在 [0.2*s, 4.0*s]，默认 12~16px 尺寸下提供 3.5x~4x 细腻特写
        double scale = Math.Clamp(fitScale, 0.2 * s, 4.0 * s);

        Bitmap? oldBmp = _bitmap;
        _bitmap = SymbolRenderer.Render(_lastStyle, _lastCorner, _lastEdge, scale);
        _emptyText = "";
        oldBmp?.Dispose();
        Invalidate();
    }

    private void EnsureChecker(int cell, int w, int h)
    {
        UiPalette palette = UiTheme.Current;
        if (_checker is not null && _checkerCell == cell && _checkerSize.Width == w && _checkerSize.Height == h && ReferenceEquals(_checkerPalette, palette))
            return;

        _checker?.Dispose();
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
        _checkerSize = new Size(w, h);
        _checkerPalette = palette;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float s = UiTheme.DpiScale(this);
        float radius = Math.Max(4f, UiTheme.DpiF(this, 8f));
        float penW = Math.Max(1f, UiTheme.DpiF(this, 1f));
        var bounds = new RectangleF(penW / 2f, penW / 2f, Width - penW, Height - penW);

        if (bounds.Width <= 2 || bounds.Height <= 2)
            return;

        using (var path = MacDraw.RoundedPath(bounds, radius))
        {
            // 在圆角视口内部裁切棋盘格
            var oldClip = g.Clip;
            g.SetClip(path);

            int cell = Math.Max(4, UiTheme.Dpi(this, 8));
            int w = Math.Max(1, Width);
            int h = Math.Max(1, Height);
            EnsureChecker(cell, w, h);
            if (_checker is not null)
                g.DrawImageUnscaled(_checker, 0, 0);

            if (_bitmap is not null)
            {
                int px = Math.Max(0, (Width - _bitmap.Width) / 2);
                int py = Math.Max(0, (Height - _bitmap.Height) / 2);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_bitmap, new Rectangle(px, py, _bitmap.Width, _bitmap.Height));
            }
            else if (_emptyText.Length > 0)
            {
                TextRenderer.DrawText(g, _emptyText, Font, ClientRectangle, UiTheme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            g.Clip = oldClip;

            // 绘制视口精致微边框（与 macOS 卡片/调色盘保持同构质感）
            using var pen = new Pen(UiTheme.CardBorder, penW);
            g.DrawPath(pen, path);
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
