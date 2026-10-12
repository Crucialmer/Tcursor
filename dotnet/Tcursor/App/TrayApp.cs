using System.Drawing;
using System.Drawing.Drawing2D;
using Tcursor.Config;
using Tcursor.Core;
using Tcursor.Render;
using Tcursor.Ui;

namespace Tcursor.App;

/// <summary>托盘应用：菜单、设置入口、生命周期</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly LayeredSymbolWindow _symbolWindow;
    private readonly CaretController _controller;
    private readonly ForegroundWatcher _watcher;
    private readonly NotifyIcon _tray;
    private readonly Icon _iconRunning;
    private readonly Icon _iconPaused;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _startupItem;
    private WindowInfoForm? _windowInfoForm;
    private readonly ContextMenuStrip _menu;
    private AppConfig _config;
    private bool _paused;

    public TrayApp()
    {
        _config = ConfigStore.Load();
        Log.SetEnabled(_config.LogEnabled);
        StartupManager.SyncPathIfNeeded();   // 开机自启：程序目录搬迁/替换为新版本文件夹后自愈为当前路径

        _symbolWindow = new LayeredSymbolWindow();
        _ = _symbolWindow.Handle; // 确保窗口句柄在 UI 线程创建

        _controller = new CaretController(_config, _symbolWindow);
        _watcher = new ForegroundWatcher(_controller.Wake);

        _toggleItem = new ToolStripMenuItem("暂停(&P)", null, (_, _) => TogglePause());
        _startupItem = new ToolStripMenuItem("开机自启动(&A)", null, (_, _) => ToggleStartup())
        {
            Checked = StartupManager.IsEnabled(),
        };

        var menu = new ContextMenuStrip { Renderer = new MacMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
        _menu = menu;
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("光标符号设置(&S)…", null, (_, _) => OpenSettings());
        menu.Items.Add("白名单规则(&W)…", null, (_, _) => OpenRules());
        menu.Items.Add("窗口信息(&I)…", null, (_, _) => OpenWindowInfo());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("打开配置文件(&C)", null, (_, _) => OpenConfigFile());
        menu.Items.Add("打开数据目录(&D)", null, (_, _) => OpenDataDir());
        menu.Items.Add("重新加载配置(&L)", null, (_, _) => ReloadConfig());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关于(&B)…", null, (_, _) => ShowAbout());
        menu.Items.Add("退出(&X)", null, (_, _) => ExitApp());

        _iconRunning = TrayIconFactory.Create(false);
        _iconPaused = TrayIconFactory.Create(true);
        _tray = new NotifyIcon
        {
            Icon = _iconRunning,
            Text = "Tcursor - 光标符号",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => TogglePause();
        UiTheme.Changed += OnThemeChanged;

        _controller.Start();
        _watcher.Start();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _controller.SetPaused(_paused);
        _toggleItem.Text = _paused ? "恢复(&R)" : "暂停(&P)";
        _tray.Text = _paused ? "Tcursor - 已暂停" : "Tcursor - 光标符号";
        _tray.Icon = _paused ? _iconPaused : _iconRunning;   // 复用缓存图标（退出时统一释放，避免每次切换泄漏一个图标句柄）
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_config);
        if (form.ShowDialog() == DialogResult.OK)
            ApplyConfig();
    }

    private void OpenRules()
    {
        using var form = new RulesForm(_config);
        if (form.ShowDialog() == DialogResult.OK)
            ApplyConfig();
    }

    private void ApplyConfig()
    {
        ConfigStore.Save(_config);
        Log.SetEnabled(_config.LogEnabled);
        _controller.UpdateConfig(_config);
    }

    private void OpenConfigFile()
    {
        ConfigStore.Save(_config); // 确保文件存在（Save 内部已容错）
        ShellOpen(ConfigStore.ConfigPath);
    }

    /// <summary>在资源管理器中定位数据目录（配置与日志所在目录）</summary>
    private void OpenDataDir()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.DataDir);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        ShellOpen(ConfigStore.DataDir);
    }

    /// <summary>用系统默认方式打开文件或目录（ShellExecute）</summary>
    private static void ShellOpen(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void ReloadConfig()
    {
        _config = ConfigStore.Load();
        Log.SetEnabled(_config.LogEnabled);
        _controller.UpdateConfig(_config);
        _startupItem.Checked = StartupManager.IsEnabled();
    }

    private void ToggleStartup()
    {
        try
        {
            if (StartupManager.IsEnabled())
                StartupManager.Disable();
            else
                StartupManager.Enable();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        _startupItem.Checked = StartupManager.IsEnabled();
    }

    /// <summary>打开「窗口信息」面板（单实例；已打开则前置）</summary>
    private void OpenWindowInfo()
    {
        if (_windowInfoForm is null || _windowInfoForm.IsDisposed)
        {
            _windowInfoForm = new WindowInfoForm(_config, ApplyConfig);
            _windowInfoForm.FormClosed += (_, _) => _windowInfoForm = null;
            _windowInfoForm.Show();
        }
        else
        {
            _windowInfoForm.Activate();
            _windowInfoForm.BringToFront();
        }
    }

    private void ShowAbout()
    {
        using var form = new AboutForm();
        form.ShowDialog();
    }

    private void OnThemeChanged() => _menu.Invalidate();

    private void ExitApp()
    {
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UiTheme.Changed -= OnThemeChanged;
            _windowInfoForm?.Dispose();
            _watcher.Dispose();
            _controller.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _iconRunning.Dispose();
            _iconPaused.Dispose();
            _symbolWindow.Dispose();
        }
        base.Dispose(disposing);
    }
}
