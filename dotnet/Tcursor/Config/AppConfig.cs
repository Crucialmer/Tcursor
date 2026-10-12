using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tcursor.Config;

/// <summary>符号形态（仅保留 无/形状；Text/Image 为历史值，加载时自动迁移为形状）</summary>
public enum SymbolKind
{
    None = 0,
    Text = 1,
    Shape = 2,
    Image = 3,
}

/// <summary>形状种类（RoundedRect 为历史值，加载时迁移为 Rect——圆角由“圆角”设置统一控制）</summary>
public enum ShapeKind
{
    Circle = 0,
    RoundedRect = 1,
    Rect = 2,
    Triangle = 3,
    Diamond = 4,
}

/// <summary>单个状态的符号样式（仅形状）</summary>
public sealed class SymbolStyle
{
    public SymbolKind Kind { get; set; } = SymbolKind.Shape;

    public ShapeKind Shape { get; set; } = ShapeKind.Rect;
    public string ShapeColor { get; set; } = "#FF0000";
    public int ShapeWidth { get; set; } = 12;    // 96 DPI 逻辑像素
    public int ShapeHeight { get; set; } = 12;

    // 通用
    /// <summary>每状态偏移（已废弃：v0.6.3 起偏移统一为全局 AppConfig.ScreenOffset*；仅旧配置迁移时读取）</summary>
    public int OffsetX { get; set; } = 0;
    /// <summary>见 OffsetX（已废弃）</summary>
    public int OffsetY { get; set; } = 0;
    public int Transparency { get; set; } = 255; // 0-255，作用于整个符号

    /// <summary>浅拷贝（保存时生成新实例，控制器按引用判定重绘）</summary>
    public SymbolStyle Clone() => (SymbolStyle)MemberwiseClone();
}

/// <summary>白名单规则（命中才显示；进程名支持正则）</summary>
public sealed class RuleConfig
{
    public string Name { get; set; } = "";
    /// <summary>进程名正则（可匹配不带 .exe 的进程名）</summary>
    public string Process { get; set; } = "";
    /// <summary>可选：窗口标题正则</summary>
    public string Title { get; set; } = "";
    /// <summary>可选：窗口类名正则</summary>
    public string Class { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>可选：捕获链（如 GUI&gt;UIA&gt;MSAA）；空 = 使用全局默认</summary>
    public string CaptureChain { get; set; } = "";
    /// <summary>可选：追加偏移（96 DPI 逻辑像素）</summary>
    public int OffsetX { get; set; } = 0;
    public int OffsetY { get; set; } = 0;
}

/// <summary>应用配置（JSON 存于数据目录）</summary>
public sealed class AppConfig
{
    /// <summary>当前样式版本（Normalize 迁移链最新段）；ConfigStore.Save 统一回写，避免过期版本号在下次加载触发误迁移</summary>
    public const int CurrentStyleVersion = 4;

    public bool Enabled { get; set; } = true;
    /// <summary>符号显示在光标的“下方”（true）或“上方”（false）</summary>
    public bool OriginBelow { get; set; } = true;
    public int PollIntervalMs { get; set; } = 20;
    /// <summary>键鼠空闲超过该时长后暂停检测（毫秒）</summary>
    public int IdleStopMs { get; set; } = 500;
    /// <summary>&gt;0 时：空闲超过该时长后隐藏符号（毫秒）</summary>
    public int HideDelayMs { get; set; } = 0;
    public string DefaultCaptureChain { get; set; } = "GUI>UIA>MSAA";
    /// <summary>全局偏移（所有状态共用，96 DPI 逻辑像素；叠加每条规则的“追加偏移”）</summary>
    public int ScreenOffsetX { get; set; } = 0;
    public int ScreenOffsetY { get; set; } = 0;
    public bool LogEnabled { get; set; } = false;

    /// <summary>形状圆角（Apple 连续曲率）：0 直角 / 1 小 16% / 2 中 25% / 3 大 33%（默认）/ 4 特大 40% / 5 超大 45% / 6 胶囊 50%</summary>
    public int ShapeCornerPreference { get; set; } = 3;
    /// <summary>样式版本：0 = v0.4 及以前，1 = v0.5，2 = v0.6 起（macOS 圆角档位 + 边框样式），3 = v0.6.3 起（偏移收敛为全局），4 = v0.7.2 起（圆角档位扩展：特大/超大，胶囊 4→6）</summary>
    public int StyleVersion { get; set; } = 0;
    /// <summary>边框样式（macOS 标准）：0 无（默认）/ 1 细描边 / 2 内高光 / 3 微立体 / 4 柔和阴影</summary>
    public int ShapeEdgeStyle { get; set; } = 0;

    public Dictionary<string, SymbolStyle> Styles { get; set; } = CreateDefaultStyles();
    public List<RuleConfig> Rules { get; set; } = new();

    /// <summary>默认形状样式（颜色取原版各状态配色）</summary>
    public static Dictionary<string, SymbolStyle> CreateDefaultStyles() => new()
    {
        ["Chinese"] = new SymbolStyle { ShapeColor = "#FF0000" },
        ["English"] = new SymbolStyle { ShapeColor = "#0000FF" },
        ["CapsLock"] = new SymbolStyle { ShapeColor = "#008000" },
        ["EnglishKeyboard"] = new SymbolStyle { ShapeColor = "#0000FF" },
        ["Japanese"] = new SymbolStyle { ShapeColor = "#CCCC00" },
        ["Korean"] = new SymbolStyle { ShapeColor = "#800080" },
    };
}

/// <summary>配置读写</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ConfigPath { get; } = ComputePath();
    public static string DataDir { get; } = Path.GetDirectoryName(ComputePath())!;

    private static bool IsPortable()
        => File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    private static string ComputePath()
    {
        // 便携模式：exe 同目录放 portable.txt
        if (IsPortable())
            return Path.Combine(AppContext.BaseDirectory, "config.json");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tcursor", "config.json");
    }

    /// <summary>一次性迁移：旧版（InputTip.Caret）配置存在且本版尚无配置时复制过来（便携模式不迁移；失败不阻断启动）</summary>
    private static void MigrateLegacyIfNeeded()
    {
        try
        {
            if (IsPortable() || File.Exists(ConfigPath))
                return;
            string legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InputTip.Caret", "config.json");
            if (File.Exists(legacy))
            {
                Directory.CreateDirectory(DataDir);
                File.Copy(legacy, ConfigPath);
            }
        }
        catch (Exception ex)
        {
            Core.Log.Error(ex);
        }
    }

    public static AppConfig Load()
    {
        try
        {
            MigrateLegacyIfNeeded();
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                AppConfig? cfg = null;
                try
                {
                    cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                }
                catch (JsonException ex)
                {
                    // 损坏的 JSON：留档（config.json.invalid）备查，随后用默认配置启动
                    Core.Log.Error(ex);
                    BackupInvalid(json);
                }
                if (cfg is not null)
                {
                    Normalize(cfg);
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            Core.Log.Error(ex);
        }
        return new AppConfig();
    }

    /// <summary>损坏配置留档（尽力而为；失败不影响启动）</summary>
    private static void BackupInvalid(string json)
    {
        try
        {
            File.WriteAllText(ConfigPath + ".invalid", json);
        }
        catch
        {
            // 忽略：留档失败不阻断启动
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            config.StyleVersion = AppConfig.CurrentStyleVersion;   // 统一回写当前版本（全新配置/旧版本内存态均由此收敛）
            Directory.CreateDirectory(DataDir);
            // 原子写：先写临时文件再整体替换——中途崩溃/断电不会留下半截 JSON（坏文件会触发 Load 回退默认）
            string tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
            File.Move(tmp, ConfigPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Core.Log.Error(ex);
        }
    }

    /// <summary>规范化：补默认状态、迁移历史形态（文本/图片/圆角矩形 → 形状）、收敛越界值</summary>
    private static void Normalize(AppConfig config)
    {
        config.ShapeCornerPreference = Math.Clamp(config.ShapeCornerPreference, 0, 6);
        config.ShapeEdgeStyle = Math.Clamp(config.ShapeEdgeStyle, 0, 4);

        // 空值守护：手改/损坏的配置（Styles 或 Rules 为 null）不应导致整份回退默认
        if (config.Styles is null)
            config.Styles = new Dictionary<string, SymbolStyle>();
        if (config.Rules is null)
            config.Rules = new List<RuleConfig>();

        if (config.StyleVersion < 1)
        {
            // v0.4 旧语义（1 直角 / 2 圆角 / 3·0 小圆角）→ v0.5 语义（0 直角 / 1 小 / 2 圆角 / 3 连续）
            config.ShapeCornerPreference = config.ShapeCornerPreference switch
            {
                1 => 0,
                2 => 2,
                _ => 3,
            };
        }
        if (config.StyleVersion < 2)
        {
            // v0.6.0–v0.6.2 的保存缺陷：写回版本号 1、值却已是新语义。
            // 新档位独有值 4（大圆角/胶囊 · 柔和阴影）出现即判定新语义——直接接受，勿再旧映射
            bool alreadyNewSemantics = config.ShapeCornerPreference >= 4 || config.ShapeEdgeStyle >= 4;
            if (!alreadyNewSemantics)
            {
                // v0.5 → v0.6：圆角档位换为 macOS 档（0 直角 / 1 小16% / 2 中25% / 3 大33% / 4 胶囊）
                config.ShapeCornerPreference = config.ShapeCornerPreference switch
                {
                    0 => 0,
                    1 => 1,
                    _ => 2,   // 圆角/连续 → 中（默认）
                };
                // 边框 → macOS 样式：模态→微立体、内嵌凹陷→内高光、静态浅边→细描边
                config.ShapeEdgeStyle = config.ShapeEdgeStyle switch
                {
                    1 => 3,
                    2 => 2,
                    3 => 1,
                    _ => 0,
                };
            }
            config.StyleVersion = 2;
        }

        if (config.StyleVersion < 3)
        {
            // v0.6.3：偏移收敛为全局——各状态偏移取首个非零值叠加到全局偏移后清零（此后样式偏移不再使用）
            int foldX = 0, foldY = 0;
            bool taken = false;
            foreach (string key in AppConfig.CreateDefaultStyles().Keys)
            {
                if (!taken && config.Styles.TryGetValue(key, out SymbolStyle? legacy) && (legacy.OffsetX != 0 || legacy.OffsetY != 0))
                {
                    foldX = legacy.OffsetX;
                    foldY = legacy.OffsetY;
                    taken = true;
                }
            }
            config.ScreenOffsetX = Math.Clamp(config.ScreenOffsetX + foldX, -500, 500);
            config.ScreenOffsetY = Math.Clamp(config.ScreenOffsetY + foldY, -500, 500);
            foreach (SymbolStyle style in config.Styles.Values)
            {
                style.OffsetX = 0;
                style.OffsetY = 0;
            }
            config.StyleVersion = 3;
        }

        if (config.StyleVersion < 4)
        {
            // v0.7.2：圆角档位扩展——新增 特大 40% / 超大 45%，胶囊索引由 4 后移为 6（旧 4 = 胶囊 → 6）
            if (config.ShapeCornerPreference == 4)
                config.ShapeCornerPreference = 6;
            config.StyleVersion = 4;
        }

        config.ScreenOffsetX = Math.Clamp(config.ScreenOffsetX, -500, 500);
        config.ScreenOffsetY = Math.Clamp(config.ScreenOffsetY, -500, 500);

        foreach (var (key, def) in AppConfig.CreateDefaultStyles())
        {
            if (!config.Styles.TryGetValue(key, out SymbolStyle? style))
            {
                config.Styles[key] = def;
                continue;
            }
            MigrateStyle(style, def);
        }
    }

    private static void MigrateStyle(SymbolStyle s, SymbolStyle def)
    {
        if (s.Kind != SymbolKind.None && s.Kind != SymbolKind.Shape)
        {
            s.Kind = SymbolKind.Shape;   // 文本/图片已移除 → 迁移为形状
            s.Shape = def.Shape;
        }
        if (s.Shape != ShapeKind.Rect)
            s.Shape = ShapeKind.Rect;    // 仅保留矩形形状（圆形/三角形/菱形/圆角矩形均为历史值）
        if (string.IsNullOrWhiteSpace(s.ShapeColor))
            s.ShapeColor = def.ShapeColor;
        s.ShapeWidth = s.ShapeWidth <= 0 ? def.ShapeWidth : Math.Clamp(s.ShapeWidth, 2, 500);
        s.ShapeHeight = s.ShapeHeight <= 0 ? def.ShapeHeight : Math.Clamp(s.ShapeHeight, 2, 500);
        s.Transparency = Math.Clamp(s.Transparency, 0, 255);
        s.OffsetX = Math.Clamp(s.OffsetX, -500, 500);
        s.OffsetY = Math.Clamp(s.OffsetY, -500, 500);
    }
}
