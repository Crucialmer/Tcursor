using Microsoft.Win32;

namespace Tcursor.Ui;

/// <summary>开机自启动（HKCU\...\Run）</summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tcursor";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>已启用且记录的路径 ≠ 当前 exe（程序目录被移动/替换为新版本文件夹）：自动改写成当前路径。启动时调用</summary>
    public static void SyncPathIfNeeded()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (key?.GetValue(ValueName) is not string value || value.Length == 0)
                return;
            if (!value.Equals($"\"{ExePath}\"", StringComparison.OrdinalIgnoreCase))
                Enable();
        }
        catch (Exception ex)
        {
            Core.Log.Error(ex);
        }
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        key?.SetValue(ValueName, $"\"{ExePath}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.DeleteValue(ValueName, false);
    }
}
