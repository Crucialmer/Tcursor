using System.Threading;
using Tcursor.App;
using Tcursor.Core;
using Tcursor.Ui;

namespace Tcursor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 自检模式：无需窗口，渲染样例 + 环境探测输出到目录
        if (args.Length >= 2 && args[0] == "--selftest")
        {
            Environment.Exit(Selftest.Run(args[1]));
            return;
        }

        // 单实例（与本软件的其它副本互斥）
        using var mutex = new Mutex(true, "Tcursor.SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

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
