using System.Reflection;

namespace Tcursor.Core;

/// <summary>发行版本信息（唯一来源：程序集 InformationalVersion——界面/自检等处不再各自硬编码）</summary>
internal static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        Assembly asm = typeof(AppInfo).Assembly;
        string version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString(3)
            ?? "0.0.0";
        int plus = version.IndexOf('+');   // 防御：去掉可能的 +commit 后缀
        return plus >= 0 ? version[..plus] : version;
    }
}
