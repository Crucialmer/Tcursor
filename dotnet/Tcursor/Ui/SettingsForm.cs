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
        ("直角 (0%)", 0), ("小圆角 (16%)", 1), ("中圆角 (25%)", 2), ("大圆角 (33% 默认)", 3),
        ("特大圆角 (40%)", 4), ("超大圆角 (45%)", 5), ("胶囊 (50%)", 6),
    };

    private static readonly (string Text, int Value)[] StateCornerOptions =
    {
        ("跟随全局", -1), ("直角 (0%)", 0), ("小圆角 (16%)", 1), ("中圆角 (25%)", 2),
        ("大圆角 (33%)", 3), ("特大圆角 (40%)", 4), ("超大圆角 (45%)", 5), ("胶囊 (50%)", 6),
    };

    private static readonly (string Text, int Value)[] EdgeOptions =
    {
        ("无 (默认)", 0), ("细描边", 1), ("内高光", 2), ("微立体", 3), ("柔和阴影", 4),
    };

    private readonly AppConfig _config;
    private readonly Dictionary<string, SymbolStyle> _edit;

    // 标签页选择器
    private readonly MacSegmentedControl _segmented = new("🎯 状态与符号", "🎨 外观与位置", "⚡ 行为与捕获", "⚙️ 系统与更新");
    private readonly Panel _contentContainer = new() { Dock = DockStyle.Fill, BackColor = UiTheme.WindowBg };
    private readonly Panel[] _panes = new Panel[4];

    // ----- Tab 0: 状态与符号 -----
    private readonly ListBox _stateList = new();
    private readonly PreviewBox _preview = new();
    private readonly MacCheck _stateOff = new() { Text = "此状态不显示光标符号", BackColor = UiTheme.CardBg };
    private readonly MacButton _colorButton = new() { Width = 125, Height = 32, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _shapeWidth = new() { Minimum = 2, Maximum = 500, Width = 80 };
    private readonly MacNumberField _shapeHeight = new() { Minimum = 2, Maximum = 500, Width = 80 };
    private readonly MacSlider _transparency = new() { Minimum = 0, Maximum = 255, Value = 255, Width = 160, BackColor = UiTheme.CardBg };
    private readonly Label _transparencyLabel = new() { AutoSize = true, ForeColor = UiTheme.Text, BackColor = UiTheme.CardBg };
    private readonly MacRadio[] _stateCornerButtons;

    // ----- Tab 1: 外观与位置 -----
    private readonly MacRadio[] _cornerButtons;
    private readonly MacRadio[] _edgeButtons;
    private readonly MacRadio _below = new() { Text = "显示在光标下方 (默认)", Checked = true, BackColor = UiTheme.CardBg };
    private readonly MacRadio _above = new() { Text = "显示在光标上方", BackColor = UiTheme.CardBg };
    private readonly MacNumberField _offsetX = new() { Minimum = -500, Maximum = 500, Width = 80, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _offsetY = new() { Minimum = -500, Maximum = 500, Width = 80, BackColor = UiTheme.CardBg };

    // ----- Tab 2: 行为与捕获 -----
    private readonly MacSwitch _enabled = new() { Text = "启用光标符号 (主开关)", BackColor = UiTheme.CardBg };
    private readonly MacNumberField _idleStop = new() { Minimum = 0, Maximum = 10000, Increment = 100, Width = 90, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _hideDelay = new() { Minimum = 0, Maximum = 10000, Increment = 100, Width = 90, BackColor = UiTheme.CardBg };
    private readonly MacNumberField _pollInterval = new() { Minimum = 5, Maximum = 200, Increment = 5, Width = 80, BackColor = UiTheme.CardBg };
    private readonly MacField _chain = new() { Width = 220, BackColor = UiTheme.CardBg };
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
        ClientSize = new Size(880, 620 + MacDialogForm.HeaderDesignPx);
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        _cornerButtons = BuildRadioGroup(CornerOptions, out FlowLayoutPanel cornerFlow);
        _edgeButtons = BuildRadioGroup(EdgeOptions, out FlowLayoutPanel edgeFlow);
        _stateCornerButtons = BuildRadioGroup(StateCornerOptions, out FlowLayoutPanel stateCornerFlow);

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

        // 初始化四大分页面板
        _panes[0] = BuildSymbolsPane(stateCornerFlow);
        _panes[1] = BuildAppearancePane(cornerFlow, edgeFlow);
        _panes[2] = BuildBehaviorPane();
        _panes[3] = BuildSystemPane();

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

        foreach (var radio in _stateCornerButtons)
            radio.CheckedChanged += (_, _) => { if (!_loading && radio.Checked) { CommitCurrent(); UpdatePreview(); } };
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

    private void ShowPane(int index)
    {
        for (int i = 0; i < _panes.Length; i++)
            _panes[i].Visible = i == index;
    }

    private Panel BuildSymbolsPane(FlowLayoutPanel stateCornerFlow)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // 左侧：状态列表卡片（带颜色圆形标点）
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

        var stateCard = new MacCard("输入法状态") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        stateCard.Controls.Add(_stateList);
        panel.Controls.Add(stateCard, 0, 0);

        // 右侧：上预览 + 下参数配置
        var rightCol = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };
        rightCol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rightCol.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        rightCol.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _preview.Dock = DockStyle.Fill;
        var previewCard = new MacCard("实时视觉预览") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        previewCard.Controls.Add(_preview);
        rightCol.Controls.Add(previewCard, 0, 0);

        var styleCard = new MacCard("当前状态样式配置") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var styleTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = UiTheme.CardBg,
        };
        styleTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        styleTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int row = 0;
        void AddStyleRow(string label, Control control)
        {
            styleTable.RowCount = row + 1;
            styleTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            styleTable.Controls.Add(new Label { Text = label, AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, row);
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
        AddStyleRow("形状圆角", stateCornerFlow);

        styleCard.Controls.Add(styleTable);
        rightCol.Controls.Add(styleCard, 0, 1);

        panel.Controls.Add(rightCol, 1, 0);
        return panel;
    }

    private Panel BuildAppearancePane(FlowLayoutPanel cornerFlow, FlowLayoutPanel edgeFlow)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            BackColor = UiTheme.WindowBg,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var cornerCard = new MacCard("全局默认圆角 (Apple Squircle 连续曲率)") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        cornerCard.Controls.Add(cornerFlow);
        panel.Controls.Add(cornerCard, 0, 0);

        var edgeCard = new MacCard("边框与立体光效 (macOS 原生效果)") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        edgeCard.Controls.Add(edgeFlow);
        panel.Controls.Add(edgeCard, 0, 1);

        var posCard = new MacCard("光标相对位置与全局偏移微调") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var posTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = UiTheme.CardBg,
        };
        posTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        posTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var posRadios = new FlowLayoutPanel { AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        posRadios.Controls.Add(_below);
        posRadios.Controls.Add(_above);

        var offsetFlow = new FlowLayoutPanel { AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        offsetFlow.Controls.Add(new Label { Text = "X:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), ForeColor = UiTheme.Dim });
        offsetFlow.Controls.Add(_offsetX);
        offsetFlow.Controls.Add(new Label { Text = "Y:", AutoSize = true, Margin = new Padding(14, 6, 4, 0), ForeColor = UiTheme.Dim });
        offsetFlow.Controls.Add(_offsetY);
        offsetFlow.Controls.Add(new Label { Text = "(像素偏移，全局所有状态共用)", AutoSize = true, Margin = new Padding(12, 6, 0, 0), ForeColor = UiTheme.Dim });

        posTable.RowCount = 2;
        posTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        posTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        posTable.Controls.Add(new Label { Text = "锚点位置", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, 0);
        posTable.Controls.Add(posRadios, 1, 0);
        posTable.Controls.Add(new Label { Text = "全局偏移", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, 1);
        posTable.Controls.Add(offsetFlow, 1, 1);
        posCard.Controls.Add(posTable);
        panel.Controls.Add(posCard, 0, 2);

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
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lifeCard = new MacCard("核心显示时机与生命周期") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        var lifeTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.CardBg };
        lifeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        lifeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int r = 0;
        void AddLifeRow(string label, Control control)
        {
            lifeTable.RowCount = r + 1;
            lifeTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lifeTable.Controls.Add(new Label { Text = label, AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, r);
            lifeTable.Controls.Add(control, 1, r);
            r++;
        }

        AddLifeRow("主运行状态", _enabled);
        AddLifeRow("空闲暂停(ms)", _idleStop);
        AddLifeRow("隐藏延时(ms)", _hideDelay);
        AddLifeRow("轮询周期(ms)", _pollInterval);
        lifeCard.Controls.Add(lifeTable);
        panel.Controls.Add(lifeCard, 0, 0);

        var captureCard = new MacCard("物理光标捕获与回退引擎") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var capTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.CardBg };
        capTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        capTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        capTable.RowCount = 2;
        capTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        capTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        capTable.Controls.Add(new Label { Text = "默认捕获链", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, 0);
        capTable.Controls.Add(_chain, 1, 0);
        capTable.Controls.Add(new Label { Text = "封闭应用回退", AutoSize = true, Font = UiTheme.FontBold, ForeColor = UiTheme.Text, Margin = new Padding(0, 8, 8, 4), BackColor = UiTheme.CardBg }, 0, 1);
        capTable.Controls.Add(_tsfFallback, 1, 1);
        captureCard.Controls.Add(capTable);
        panel.Controls.Add(captureCard, 0, 1);

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
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var updateCard = new MacCard("软件自动更新") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        var updateBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = UiTheme.CardBg, Margin = new Padding(0) };
        updateBox.Controls.Add(_updateAutoCheck);
        updateBox.Controls.Add(_updateAutoDownload);
        updateCard.Controls.Add(updateBox);
        panel.Controls.Add(updateCard, 0, 0);

        var dataCard = new MacCard("排障与数据存储") { Dock = DockStyle.Fill, Margin = new Padding(0) };
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
        dataCard.Controls.Add(dataTable);
        panel.Controls.Add(dataCard, 0, 1);

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
        flow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0), BackColor = UiTheme.CardBg };
        for (int i = 0; i < options.Length; i++)
        {
            radios[i] = new MacRadio { Text = options[i].Text, BackColor = UiTheme.CardBg, Margin = new Padding(0, 4, 14, 4) };
            flow.Controls.Add(radios[i]);
        }
        return radios;
    }

    private void ApplyDpiMetrics()
    {
        int itemHeight = UiTheme.Dpi(_stateList, 40);
        if (_stateList.ItemHeight != itemHeight)
            _stateList.ItemHeight = itemHeight;
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
            SetRadio(_stateCornerButtons, StateCornerOptions, Math.Clamp(s.CornerPreference, -1, 6));
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
        s.CornerPreference = CurrentRadio(_stateCornerButtons, StateCornerOptions, -1);
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
            styles[key] = style.Clone();
        _config.Styles = styles;
    }

    private void UpdatePreview()
    {
        if (_loading)
            return;
        int stateCorner = CurrentRadio(_stateCornerButtons, StateCornerOptions, -1);
        int globalCorner = CurrentRadio(_cornerButtons, CornerOptions, 3);
        int effectiveCorner = stateCorner >= 0 ? stateCorner : globalCorner;
        var temp = new SymbolStyle
        {
            Kind = _stateOff.Checked ? SymbolKind.None : SymbolKind.Shape,
            Shape = ShapeKind.Rect,
            ShapeColor = _colorButton.Text.Trim(),
            ShapeWidth = (int)_shapeWidth.Value,
            ShapeHeight = (int)_shapeHeight.Value,
            Transparency = _transparency.Value,
            CornerPreference = stateCorner,
        };
        _preview.SetSymbol(temp, effectiveCorner, CurrentRadio(_edgeButtons, EdgeOptions, 0));
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
            SetRadio(_stateCornerButtons, StateCornerOptions, -1);
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
        foreach (var radio in _stateCornerButtons)
            radio.Enabled = on;
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

/// <summary>实时预览盒：高精度棋盘格背景 + 渲染位图（放大无插值，保持像素边缘真实观感）</summary>
internal sealed class PreviewBox : Control
{
    private Bitmap? _bitmap;
    private string _emptyText = "";

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
            _emptyText = "此状态不显示光标符号";
        }
        else
        {
            double s = UiTheme.DpiScale(this);
            double maxSide = Math.Max(1, Math.Max(style.ShapeWidth, style.ShapeHeight));
            double scale = Math.Min(4.0 * s, UiTheme.Dpi(this, 160) / maxSide);
            if (scale < 0.2 * s)
                scale = 0.2 * s;
            _bitmap = SymbolRenderer.Render(style, corner, edge, scale);
            _emptyText = "";
        }
        old?.Dispose();
        Invalidate();
    }

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
