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
    private readonly ToolStripMenuItem _updateItem;
    private readonly MacMenuHeaderItem _brandHeader;
    private readonly SynchronizationContext _sync = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
    private WindowInfoForm? _windowInfoForm;
    private readonly ContextMenuStrip _menu;
    private AppConfig _config;
    private bool _paused;
    private bool _updateBusy;
    private int _exitInstallDone;   // Interlocked：SessionEnding（系统事件线程）与退出路径（UI 线程）可能并发进入
    private bool _downloading;   // 后台静默下载进行中（期间禁止再次发起下载）
    private int _checkInFlight;  // 检查互斥（跨线程：覆盖手动与自动两路检查，避免互相覆盖菜单文案）
    private readonly Microsoft.Win32.SessionEndingEventHandler _onSessionEnding;
    private UpdateInfo? _pendingUpdate;   // 自动检查发现的可用更新（供气泡点击/手动检查复用）
    private string? _preparedPackage;     // 已下载并校验通过的更新包（静默下载完成后）

    public TrayApp()
    {
        _config = ConfigStore.Load();
        Log.SetEnabled(_config.LogEnabled);
        StartupManager.SyncPathIfNeeded();   // 开机自启：程序目录搬迁/替换为新版本文件夹后自愈为当前路径

        _symbolWindow = new LayeredSymbolWindow();
        _ = _symbolWindow.Handle; // 确保窗口句柄在 UI 线程创建

        _controller = new CaretController(_config, _symbolWindow);
        _watcher = new ForegroundWatcher(_controller.Wake);

        _brandHeader = new MacMenuHeaderItem();
        _brandHeader.Click += (_, _) => OpenSettings();

        ToolStripMenuItem MakeItem(string text, EventHandler? onClick = null)
            => new(text, null, onClick) { Font = UiTheme.FontMenu, Padding = new Padding(8, 6, 8, 6) };

        _toggleItem = MakeItem("暂停", (_, _) => TogglePause());
        _startupItem = MakeItem("开机自启动", (_, _) => ToggleStartup());
        _startupItem.Checked = StartupManager.IsEnabled();

        var menu = new MacContextMenuStrip();
        _menu = menu;
        menu.MinimumSize = new Size(UiTheme.Dpi(menu, 168), 0);
        menu.HandleCreated += (_, _) =>
        {
            try
            {
                int pref = Win32.NativeMethods.DWMWCP_ROUND;
                _ = Win32.NativeMethods.DwmSetWindowAttribute(menu.Handle, Win32.NativeMethods.DWMWA_WINDOW_CORNER_RADIUS, ref pref, sizeof(int));
                int darkVal = UiTheme.IsDark ? 1 : 0;
                _ = Win32.NativeMethods.DwmSetWindowAttribute(menu.Handle, Win32.NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        };
        menu.Opening += (_, _) =>
        {
            try
            {
                int darkVal = UiTheme.IsDark ? 1 : 0;
                _ = Win32.NativeMethods.DwmSetWindowAttribute(menu.Handle, Win32.NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));
            }
            catch { }
            menu.MinimumSize = new Size(UiTheme.Dpi(menu, 168), 0);
            _brandHeader.Size = new Size(UiTheme.Dpi(menu, 168), UiTheme.Dpi(menu, 46));
            _brandHeader.UpdateStatus(_paused, _config.Rules.Count(r => r.Enabled));
        };

        menu.Items.Add(_brandHeader);
        menu.Items.Add(new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) });
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) });
        menu.Items.Add(MakeItem("光标符号设置…", (_, _) => OpenSettings()));
        menu.Items.Add(MakeItem("白名单规则…", (_, _) => OpenRules()));
        menu.Items.Add(MakeItem("窗口信息…", (_, _) => OpenWindowInfo()));
        menu.Items.Add(new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) });
        menu.Items.Add(MakeItem("打开配置文件", (_, _) => OpenConfigFile()));
        menu.Items.Add(MakeItem("打开数据目录", (_, _) => OpenDataDir()));
        menu.Items.Add(MakeItem("重新加载配置", (_, _) => ReloadConfig()));
        if (!IsElevated())
        {
            menu.Items.Add(MakeItem("以管理员身份重启", (_, _) => RestartAsAdmin()));
        }
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) });
        _updateItem = MakeItem("检查更新…", (_, _) => OnUpdateMenu());
        menu.Items.Add(_updateItem);
        menu.Items.Add(MakeItem("关于 Tcursor…", (_, _) => ShowAbout()));
        menu.Items.Add(MakeItem("退出 Tcursor", (_, _) => ExitApp()));

        _iconRunning = TrayIconFactory.Create(false);
        _iconPaused = TrayIconFactory.Create(true);
        _tray = new NotifyIcon
        {
            Icon = _iconRunning,
            Text = $"Tcursor v{AppInfo.Version} - 光标符号提示",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => TogglePause();
        _tray.BalloonTipClicked += (_, _) => OfferPendingUpdate();
        UiTheme.Changed += OnThemeChanged;

        _controller.Start();
        _watcher.Start();

        _onSessionEnding = (_, _) => TryInstallOnExit();   // 注销/关机时也把已就绪的更新装好
        try
        {
            Microsoft.Win32.SystemEvents.SessionEnding += _onSessionEnding;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        // 启动时若已有「上次退出前下载就绪」的更新包，先提示一次（并让菜单项反映可更新状态）
        // 一次拿回「清单 + 包路径」：原先 ReadPrepared + PreparedPackage 会对 46MB 包各做一次全量 sha256
        if (UpdateService.ReadPreparedWithPackage() is { } preparedNow)
        {
            _pendingUpdate = preparedNow.Info;
            _preparedPackage = preparedNow.Package;
            _updateItem.Text = $"更新到 {preparedNow.Info.Version}…";
        }

        if (_config.UpdateAutoCheck)
            Task.Run(AutoCheckForUpdates);   // 启动后台检查（每日一次；不阻塞启动）
    }

    // ---------------- 检查更新 ----------------

    /// <summary>菜单项：已有就绪包则直接进入安装确认（不联网），否则发起检查。</summary>
    private void OnUpdateMenu()
    {
        if (_downloading)
        {
            MacDialog.Alert(null, "更新正在后台下载，下载完成后即可安装。", "更新");
            return;
        }
        bool ready = _preparedPackage is not null;   // 就绪包在下载完成时已记录路径；不再每次点击都重算 46MB 哈希
        if (ready)
            OfferPendingUpdate();
        else
            CheckForUpdates(manual: true);
    }

    /// <summary>启动时的后台检查：距上次检查不足 20 小时则跳过；
    /// 发现新版本后：档①（默认）= 后台静默下载，下载好再提示「已就绪」；关闭自动下载则只提示。</summary>
    private void AutoCheckForUpdates()
    {
        if (Interlocked.CompareExchange(ref _checkInFlight, 1, 0) != 0)
            return;   // 已有检查在跑（手动/自动）：避免两路检查并发互相覆盖菜单文案
        try
        {
            if (DateTime.UtcNow.Ticks - _config.UpdateLastCheckUtc < TimeSpan.FromHours(20).Ticks)
                return;
            UpdateInfo? info = UpdateService.Check(out string error);
            if (info is null)
            {
                Log.Info("自动检查更新失败：" + error);
                return;
            }
            _sync.Post(_ =>
            {
                if (!info.Newer)
                {
                    MarkChecked();   // 检查完成且无需下载：记录节流时间戳
                    return;
                }
                _pendingUpdate = info;
                if (_config.UpdateAutoDownload)
                {
                    _downloading = true;
                    _updateItem.Text = "正在下载更新…";
                    _updateItem.Enabled = false;
                    Task.Run(() => SilentDownload(info));   // 时间戳留到下载有结果再记（失败则下次启动立即重试）
                    return;
                }
                MarkChecked();
                _tray.ShowBalloonTip(10000, "Tcursor 有新版本",
                    $"发现 {info.Version}（当前 {AppInfo.Version}）——点击此处更新", ToolTipIcon.Info);
            }, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex);   // 自动检查失败静默（只记日志）
        }
        finally
        {
            Interlocked.Exchange(ref _checkInFlight, 0);
        }
    }

    /// <summary>记录「检查 + （开启自动下载时）下载成功」的时刻——20 小时节流的依据。
    /// 下载失败**不记录**：否则一次网络抖动会把用户冻结到 20 小时后再有机会更新。</summary>
    private void MarkChecked()
    {
        _config.UpdateLastCheckUtc = DateTime.UtcNow.Ticks;
        ConfigStore.Save(_config);
    }

    /// <summary>后台静默下载 + 校验（档①）。完成后气泡提示：点气泡立即更新；不理会则退出或下次启动时自动装好。</summary>
    private void SilentDownload(UpdateInfo info)
    {
        try
        {
            string? package = UpdateService.Download(info, null, CancellationToken.None, out string error);
            if (package is null)
            {
                Log.Info("后台下载更新失败（下次启动会自动重试）：" + error);
                return;
            }
            UpdateService.MarkPrepared(info);
            _sync.Post(_ =>
            {
                _preparedPackage = package;
                _downloading = false;
                _updateItem.Enabled = true;
                _updateItem.Text = $"更新到 {info.Version}…";
                MarkChecked();   // 下载成功：记时间戳（此后由退出/启动时安装接管）
                _tray.ShowBalloonTip(10000, "Tcursor 更新已就绪",
                    $"新版本 {info.Version} 已下载并校验完成：点此立即更新；不理会也会在退出或下次启动时自动装好", ToolTipIcon.Info);
            }, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        finally
        {
            // 无论成败都退出「下载中」状态（成功路径已在上面把文案改为「更新到 x…」）
            _sync.Post(_ =>
            {
                if (!_downloading)
                    return;
                _downloading = false;
                _updateItem.Enabled = true;
                if (_preparedPackage is null)
                    _updateItem.Text = "检查更新…";
            }, null);
        }
    }

    /// <summary>检查更新（手动 = 有明确反馈；自动 = 静默）。</summary>
    private void CheckForUpdates(bool manual)
    {
        if (_updateBusy || Interlocked.CompareExchange(ref _checkInFlight, 1, 0) != 0)
            return;
        _updateBusy = true;
        _updateItem.Enabled = false;
        _updateItem.Text = "正在检查更新…";
        Task.Run(() =>
        {
            UpdateInfo? info = UpdateService.Check(out string error);
            _sync.Post(_ =>
            {
                _updateBusy = false;
                Interlocked.Exchange(ref _checkInFlight, 0);
                _updateItem.Enabled = true;
                if (!_downloading)
                    _updateItem.Text = "检查更新…";
                OnChecked(info, error, manual);
            }, null);
        });
    }

    private void OnChecked(UpdateInfo? info, string error, bool manual)
    {
        if (info is null)
        {
            if (manual)
                MacDialog.Alert(null, "检查更新失败：" + error, "检查更新");
            return;
        }
        _config.UpdateLastCheckUtc = DateTime.UtcNow.Ticks;
        ConfigStore.Save(_config);
        if (!info.Newer)
        {
            if (manual)
                MacDialog.Alert(null, $"当前已是最新版本（{AppInfo.Version}）。", "检查更新");
            return;
        }
        _pendingUpdate = info;
        OfferPendingUpdate();
    }

    /// <summary>更新确认与执行：已有就绪包 → 直接安装（带重启）；否则下载（进度）再安装。</summary>
    private void OfferPendingUpdate()
    {
        UpdateInfo? info = _pendingUpdate;
        if (info is null)
            return;

        string? prepared = _preparedPackage ?? UpdateService.PreparedPackage(info);
        if (prepared is not null)
        {
            if (!MacDialog.Confirm(null,
                    $"新版本 {info.Version} 已下载并校验完成（当前 {AppInfo.Version}）。" + Environment.NewLine + Environment.NewLine +
                    "现在更新并重启？约 1 秒。不理会的话，退出 Tcursor 或下次启动时也会自动装好。",
                    "更新已就绪"))
                return;
            FinishUpdate(prepared, restart: true);
            return;
        }

        if (!MacDialog.Confirm(null,
                $"发现新版本 {info.Version}（当前 {AppInfo.Version}）。" + Environment.NewLine + Environment.NewLine +
                "立即下载并更新？更新完成后程序会自动重启。",
                "发现新版本"))
            return;

        if (!UpdateService.CanSelfUpdate(out string why))
        {
            bool open = MacDialog.Confirm(null, why + Environment.NewLine + Environment.NewLine +
                "是否现在打开发布页，手动下载后覆盖当前目录？", "无法自动更新");
            if (open)
                ShellOpen(UpdateService.ReleasesPage);
            return;
        }

        using var progress = new UpdateProgressForm(info);
        if (progress.ShowDialog() != DialogResult.OK)
        {
            if (!progress.Cancelled)
                MacDialog.Alert(null, "更新未完成：" + (progress.Error.Length > 0 ? progress.Error : "未知原因"), "更新");
            return;
        }
        FinishUpdate(progress.PackagePath!, restart: true);
    }

    /// <summary>把更新包交给更新器完成替换；<paramref name="restart"/>=false 用于退出时自动安装（下次启动即新版本）。</summary>
    private void FinishUpdate(string package, bool restart)
    {
        if (!UpdateService.SpawnUpdater(package, out string error, restart))
        {
            if (restart)
                MacDialog.Alert(null,
                    "无法启动更新器：" + error + Environment.NewLine + Environment.NewLine +
                    "已下载的更新包：" + Environment.NewLine + package + Environment.NewLine + Environment.NewLine +
                    "可手动解压覆盖当前目录。", "更新失败");
            return;
        }
        _tray.Visible = false;
        ExitThread();   // 退出主进程：更新器接管替换（与可选重启）
    }

    /// <summary>退出前：若已有「已下载并校验通过」的更新包且允许自动安装，则交给更新器（**不重启**，
    /// 下次启动即为新版本）。这是档①的落点：使用中永不重启，更新在退出时悄悄装好。</summary>
    private void TryInstallOnExit()
    {
        if (!_config.UpdateAutoDownload)
            return;
        // 注销/关机（SystemEvents 专用线程）与点「退出」（UI 线程）可能同时进来 —— 必须原子地只放行一次，
        // 否则两个更新器实例会互相撞锁（第二个复制自更新器时第一个已在运行）。
        if (Interlocked.Exchange(ref _exitInstallDone, 1) != 0)
            return;
        try
        {
            PreparedInstallResult result = UpdateService.InstallPrepared(out string error, restart: false);
            if (result == PreparedInstallResult.Started)
                Log.Info("退出时已交付更新器安装（下次启动为新版本）");
            else if (result == PreparedInstallResult.Failed)
                Log.Info("退出时安装跳过：" + error);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _controller.SetPaused(_paused);
        _toggleItem.Text = _paused ? "恢复" : "暂停";
        _tray.Text = _paused ? "Tcursor - 已暂停" : "Tcursor - 光标符号";
        _tray.Icon = _paused ? _iconPaused : _iconRunning;   // 复用缓存图标（退出时统一释放，避免每次切换泄漏一个图标句柄）
        _brandHeader.UpdateStatus(_paused, _config.Rules.Count(r => r.Enabled));
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
        _brandHeader.UpdateStatus(_paused, _config.Rules.Count(r => r.Enabled));
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
        if (form.ShowDialog() == DialogResult.Abort)   // 「检查更新…」按钮
            CheckForUpdates(manual: true);
    }

    private void OnThemeChanged()
    {
        _tray.Icon = _paused ? _iconPaused : _iconRunning;
        _brandHeader.Invalidate();
        _menu.Invalidate(true);
    }

    private void ExitApp()
    {
        TryInstallOnExit();   // 档①：退出时把已就绪的更新装好（下次启动即新版本，不重启）
        _tray.Visible = false;
        ExitThread();
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void RestartAsAdmin()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Application.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas",
            };
            System.Diagnostics.Process.Start(psi);
            Application.Exit();
        }
        catch
        {
            // 用户取消 UAC 授权提示
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UiTheme.Changed -= OnThemeChanged;
            try
            {
                Microsoft.Win32.SystemEvents.SessionEnding -= _onSessionEnding;   // 静态事件：不退订会让本实例被静态引用
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
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
