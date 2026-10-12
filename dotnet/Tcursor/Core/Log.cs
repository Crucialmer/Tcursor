namespace Tcursor.Core;

/// <summary>轻量文件日志（默认仅记录错误；可在此扩展）</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static bool _enabled;
    private static string? _filePath;

    public static string? FilePath => _filePath;

    /// <summary>按配置启用/停用日志写入（默认由配置的“写入日志文件”开关决定）</summary>
    public static void SetEnabled(bool enabled) => _enabled = enabled;

    public static void Init()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tcursor");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "log.txt");
            // 简单轮转：超过 1MB 清空重来
            var fi = new FileInfo(_filePath);
            if (fi.Exists && fi.Length > 1024 * 1024)
                fi.Delete();
            _enabled = true;
        }
        catch
        {
            _enabled = false;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(Exception? exception) => Write("ERROR", exception?.ToString() ?? "未知错误");

    private static void Write(string level, string message)
    {
        if (!_enabled || _filePath is null)
            return;
        try
        {
            lock (Gate)
                File.AppendAllText(_filePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
