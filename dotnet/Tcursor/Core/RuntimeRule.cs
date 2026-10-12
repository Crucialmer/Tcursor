using System.Text.RegularExpressions;
using Tcursor.Config;

namespace Tcursor.Core;

/// <summary>
/// 已编译的白名单规则：加载/保存配置时构建一次，避免每帧重新解析与缓存查找。
/// </summary>
internal sealed class RuntimeRule
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    public string Name { get; init; } = "";
    public Regex? Process { get; init; }
    public Regex? Title { get; init; }
    public Regex? Class { get; init; }
    public string CaptureChain { get; init; } = "";
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }

    /// <summary>编译规则；无效（正则为空或编译失败）返回 null</summary>
    public static RuntimeRule? TryCompile(RuleConfig rule)
    {
        bool hasProcess = !string.IsNullOrWhiteSpace(rule.Process);
        bool hasTitle = !string.IsNullOrWhiteSpace(rule.Title);
        bool hasClass = !string.IsNullOrWhiteSpace(rule.Class);
        if (!hasProcess && !hasTitle && !hasClass)
            return null;

        try
        {
            return new RuntimeRule
            {
                Name = rule.Name,
                Process = hasProcess ? Compile(rule.Process) : null,
                Title = hasTitle ? Compile(rule.Title) : null,
                Class = hasClass ? Compile(rule.Class) : null,
                CaptureChain = rule.CaptureChain ?? "",
                OffsetX = rule.OffsetX,
                OffsetY = rule.OffsetY,
            };
        }
        catch (Exception ex)
        {
            Log.Error($"规则「{rule.Name}」正则无效，已忽略：{ex.Message}");
            return null;
        }
    }

    private static Regex Compile(string pattern)
        => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    /// <summary>进程名同时按“带 .exe”与“不带 .exe”匹配（调用方预生成 exe 名，避免每规则分配）</summary>
    public bool Matches(string process, string processExe, string title, string cls)
    {
        if (Process is not null && !Process.IsMatch(process) && !Process.IsMatch(processExe))
            return false;
        if (Title is not null && !Title.IsMatch(title))
            return false;
        if (Class is not null && !Class.IsMatch(cls))
            return false;
        return true;
    }
}
