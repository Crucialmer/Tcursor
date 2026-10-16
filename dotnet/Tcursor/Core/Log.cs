namespace Tcursor.Core;

/// <summary>轻量文件日志（默认仅记录错误；可在此扩展）</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;   // 单文件上限：启动时超限清空 + 会话内累计超限轮转

    private static readonly object Gate = new();
    private static bool _enabled;
    private static string? _filePath;
    private static long _written;

    public static string? FilePath => _filePath;

    /// <summary>按配置启用/停用日志写入（默认由配置的“写入日志文件”开关决定）</summary>
    public static void SetEnabled(bool enabled) => _enabled = enabled;

    public static void Init()
    {
        try
        {
            var dir = Config.ConfigStore.DataDir;
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "log.txt");
            // 简单轮转：超过 1MB 清空重来
            var fi = new FileInfo(_filePath);
            if (fi.Exists && fi.Length > MaxBytes)
            {
                fi.Delete();
                fi.Refresh();
            }
            _written = fi.Exists ? fi.Length : 0;
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
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(_filePath, line);
                _written += System.Text.Encoding.UTF8.GetByteCount(line);   // 按落盘字节数累计（中文错误消息 1 字符≈3 字节）
                if (_written > MaxBytes)
                    Rotate();
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }

    /// <summary>会话内轮转：超限即清空重来（与 Init 的超限策略一致）——
    /// 否则某条异常每 tick 复现时日志会在单次会话内无界增长（异常路径见 CaretController.WorkerLoop 的兜底捕获）</summary>
    private static void Rotate()
    {
        try
        {
            File.Delete(_filePath!);
        }
        catch
        {
            // 删除失败（文件被占用等）：下次写入再试
        }
        _written = 0;
    }
}
