using System.Threading;
using Tcursor.App;
using Tcursor.Config;
using Tcursor.Core;
using Tcursor.Ui;

namespace Tcursor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 更新器模式：由正在运行的实例启动（等旧进程退出 → 替换 exe → 视参数重启）。
        // 必须早于单实例互斥：更新器与被替换文件无占用关系，不应受「已有实例」影响。
        if (args.Length >= 3 && args[0] == "--apply-update")
        {
            int waitPid = args.Length > 3 && int.TryParse(args[3], out int parsed) ? parsed : 0;
            bool restart = !args.Contains("--no-restart");
            Environment.Exit(UpdateService.ApplyUpdate(args[1], args[2], waitPid, restart));
            return;
        }

        // 自检模式：无需窗口，渲染样例 + 环境探测输出到目录
        if (args.Length >= 1 && args[0] == "--selftest")
        {
            string outDir = args.Length >= 2 ? args[1] : Path.Combine(Path.GetTempPath(), "tcursor-selftest");
            Environment.Exit(Selftest.Run(outDir));
            return;
        }

        // 无界面更新检查 / 更新（自动化与部署验证用；结果写 JSON 文件）
        if (args.Length >= 2 && args[0] == "--check-update")
        {
            Log.Init();
            Environment.Exit(UpdateService.CheckCli(args[1]));
            return;
        }
        if (args.Length >= 2 && args[0] == "--update-now")
        {
            Log.Init();
            Environment.Exit(UpdateService.UpdateNowCli(args[1]));
            return;
        }
        if (args.Length >= 2 && args[0] == "--prepare-update")
        {
            Log.Init();
            Environment.Exit(UpdateService.PrepareCli(args[1]));
            return;
        }
        if (args.Length >= 2 && args[0] == "--install-prepared")
        {
            Log.Init();
            Environment.Exit(UpdateService.InstallPreparedCli(args[1]));
            return;
        }

        // 单实例（与本软件的其它副本互斥）
        using var mutex = new Mutex(true, "Tcursor.SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        // 启动时安装（v1.3）：本机已有「已就绪」且比当前版本更新的更新包 → 现在装好并重启一次（约 2 秒），
        // 这样「强制结束」「关机超时」「上次退出时还没下好」这几种情况也能在下次启动时补上。
        string startupNote = "";
        if (ConfigStore.Load().UpdateAutoDownload && UpdateService.TryInstallPreparedOnStartup(out startupNote))
        {
            Log.Init();
            Log.Info("启动时安装：" + startupNote);
            return;   // 更新器接管：等本进程退出 → 替换 → 启动新版本
        }

        Log.Init();
        Application.ThreadException += (_, e) => Log.Error(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);   // 原生控件/系统对话框跟随系统深浅色（WinForms 深色模式）
        UiTheme.DetectAndApply();                           // 自绘界面同步跟随

        using var app = new TrayApp();
        Application.Run(app);
    }
}
