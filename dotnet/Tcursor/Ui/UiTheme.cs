using System.Drawing;
using Microsoft.Win32;

namespace Tcursor.Ui;

/// <summary>一套界面调色板（浅色/深色共用同一组语义槽位）</summary>
internal sealed class UiPalette
{
    // 基础面
    public Color WindowBg;
    public Color CardBg;
    public Color CardBorder;
    public Color Accent;
    public Color AccentHover;
    public Color AccentDown;
    public Color AccentDisabled;
    public Color Text;
    public Color Dim;
    public Color SectionTitle;
    public Color ControlBorder;
    public Color TrackBg;

    // 次要按钮（仅绘制用）
    public Color ButtonFace;
    public Color ButtonHover;
    public Color ButtonDown;
    public Color ButtonDisabledFace;

    // 列表 / 预览
    public Color CheckerEven;
    public Color CheckerOdd;

    // 窗口标题区（macOS 风格 chrome）
    public Color HeaderTitle;
    public Color HeaderTitleInactive;
    public Color HeaderLine;
    public Color TrafficRed;
    public Color TrafficYellow;
    public Color TrafficGreen;
    public Color TrafficInactive;
    public Color TrafficGlyph;

    // 托盘菜单
    public Color MenuBack;
    public Color MenuBorder;
    public Color MenuText;
    public Color MenuHighlightBack;
    public Color MenuHighlightText;

    public static UiPalette CreateLight() => new()
    {
        WindowBg = Color.FromArgb(0xF5, 0xF5, 0xF7),
        CardBg = Color.White,
        CardBorder = Color.FromArgb(0xE5, 0xE5, 0xEA),
        Accent = Color.FromArgb(0x00, 0x7A, 0xFF),
        AccentHover = Color.FromArgb(0x33, 0x95, 0xFF),
        AccentDown = Color.FromArgb(0x00, 0x62, 0xCC),
        AccentDisabled = Color.FromArgb(0xED, 0xED, 0xF0),   // macOS：禁用态主按钮 = 中性灰（不再用淡蓝）
        Text = Color.FromArgb(0x1D, 0x1D, 0x1F),
        Dim = Color.FromArgb(0x8E, 0x8E, 0x93),
        SectionTitle = Color.FromArgb(0x6E, 0x6E, 0x73),
        ControlBorder = Color.FromArgb(0xD1, 0xD1, 0xD6),
        TrackBg = Color.FromArgb(0xE9, 0xE9, 0xEB),
        ButtonFace = Color.White,
        ButtonHover = Color.FromArgb(0xFA, 0xFA, 0xFC),
        ButtonDown = Color.FromArgb(0xE5, 0xE5, 0xEA),
        ButtonDisabledFace = Color.FromArgb(0xF5, 0xF5, 0xF7),
        CheckerEven = Color.White,
        CheckerOdd = Color.FromArgb(0xF1, 0xF1, 0xF4),
        HeaderTitle = Color.FromArgb(0x3C, 0x3C, 0x43),
        HeaderTitleInactive = Color.FromArgb(0xA6, 0xA6, 0xAC),
        HeaderLine = Color.FromArgb(0xE5, 0xE5, 0xEA),
        TrafficRed = Color.FromArgb(0xFF, 0x5F, 0x57),
        TrafficYellow = Color.FromArgb(0xFE, 0xBC, 0x2E),
        TrafficGreen = Color.FromArgb(0x28, 0xC8, 0x40),
        TrafficInactive = Color.FromArgb(0xD6, 0xD6, 0xDA),
        TrafficGlyph = Color.FromArgb(0x99, 0x0C, 0x02),
        MenuBack = Color.White,
        MenuBorder = Color.FromArgb(0xE0, 0xE0, 0xE5),
        MenuText = Color.FromArgb(0x1D, 0x1D, 0x1F),
        MenuHighlightBack = Color.FromArgb(0x00, 0x7A, 0xFF),
        MenuHighlightText = Color.White,
    };

    public static UiPalette CreateDark() => new()
    {
        WindowBg = Color.FromArgb(0x1E, 0x1E, 0x20),
        CardBg = Color.FromArgb(0x2C, 0x2C, 0x2E),
        CardBorder = Color.FromArgb(0x3A, 0x3A, 0x3C),
        Accent = Color.FromArgb(0x0A, 0x84, 0xFF),
        AccentHover = Color.FromArgb(0x3D, 0x9D, 0xFF),
        AccentDown = Color.FromArgb(0x00, 0x66, 0xD6),
        AccentDisabled = Color.FromArgb(0x3A, 0x3A, 0x3C),   // macOS：禁用态主按钮 = 中性灰（不再用深蓝）
        Text = Color.FromArgb(0xF2, 0xF2, 0xF7),
        Dim = Color.FromArgb(0x98, 0x98, 0x9D),
        SectionTitle = Color.FromArgb(0xAE, 0xAE, 0xB2),
        ControlBorder = Color.FromArgb(0x48, 0x48, 0x4A),
        TrackBg = Color.FromArgb(0x3A, 0x3A, 0x3C),
        ButtonFace = Color.FromArgb(0x38, 0x38, 0x3A),
        ButtonHover = Color.FromArgb(0x44, 0x44, 0x46),
        ButtonDown = Color.FromArgb(0x50, 0x50, 0x52),
        ButtonDisabledFace = Color.FromArgb(0x2C, 0x2C, 0x2E),
        CheckerEven = Color.FromArgb(0x2C, 0x2C, 0x2E),
        CheckerOdd = Color.FromArgb(0x24, 0x24, 0x26),
        HeaderTitle = Color.FromArgb(0xD8, 0xD8, 0xDE),
        HeaderTitleInactive = Color.FromArgb(0x6E, 0x6E, 0x73),
        HeaderLine = Color.FromArgb(0x3A, 0x3A, 0x3C),
        TrafficRed = Color.FromArgb(0xFF, 0x5F, 0x57),
        TrafficYellow = Color.FromArgb(0xFE, 0xBC, 0x2E),
        TrafficGreen = Color.FromArgb(0x28, 0xC8, 0x40),
        TrafficInactive = Color.FromArgb(0x48, 0x48, 0x4A),
        TrafficGlyph = Color.FromArgb(0x66, 0x08, 0x01),
        MenuBack = Color.FromArgb(0x2A, 0x2A, 0x2C),
        MenuBorder = Color.FromArgb(0x3C, 0x3C, 0x3E),
        MenuText = Color.FromArgb(0xF2, 0xF2, 0xF7),
        MenuHighlightBack = Color.FromArgb(34, 255, 255, 255), // macOS 原厂风格：微光白半透明胶囊 rgba(255, 255, 255, 34)
        MenuHighlightText = Color.White,
    };
}

/// <summary>
/// 界面主题：调色板（浅色/深色）+ 跟随系统深浅色 + 已打开窗口的实时换肤。
/// 颜色属性一律经 <see cref="Current"/> 转发——自绘控件在绘制时实时取值。
/// </summary>
internal static class UiTheme
{
    /// <summary>设计基线 DPI：全部界面像素常量按 96 DPI 设计。</summary>
    public const float DesignDpi = 96f;

    private static readonly string PrimaryFontName = ResolveFontFamily();
    private static readonly string MonoFontName = ResolveMonoFamily();
    private static readonly string MenuFontName = ResolveMenuFontFamily();

    public static string PrimaryFontFamily => PrimaryFontName;

    private static string ResolveFontFamily()
    {
        // 优先 Segoe UI Variable Text（Windows 11 原生，英数字形排版兼具果味与现代感），中文平滑回退微软雅黑
        try
        {
            using var test = new FontFamily("Segoe UI Variable Text");
            return "Segoe UI Variable Text";
        }
        catch
        {
            return "Microsoft YaHei UI";
        }
    }

    private static string ResolveMenuFontFamily()
    {
        // 优先微软雅黑 UI（Microsoft YaHei UI），笔画端庄规整，呈现典雅标准的简体中文排版
        string[] candidates = { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Segoe UI" };
        foreach (var name in candidates)
        {
            try
            {
                using var test = new FontFamily(name);
                return name;
            }
            catch { }
        }
        return PrimaryFontName;
    }

    private static string ResolveMonoFamily()
    {
        // 优先 Cascadia Mono / Consolas 等现代等宽字体，用于展示类名、路径、状态码等数据
        string[] candidates = { "Cascadia Mono", "Consolas", "Courier New" };
        foreach (var name in candidates)
        {
            try
            {
                using var test = new FontFamily(name);
                return name;
            }
            catch { }
        }
        return FontFamily.GenericMonospace.Name;
    }

    /// <summary>主正文字体（9.5pt 常规）</summary>
    public static readonly Font Font = new(PrimaryFontName, 9.5f);
    /// <summary>主粗体（9.5pt 粗体）</summary>
    public static readonly Font FontBold = new(PrimaryFontName, 9.5f, FontStyle.Bold);
    /// <summary>分段卡片标题（10.5pt 粗体，强化结构层级）</summary>
    public static readonly Font FontSectionTitle = new(PrimaryFontName, 10.5f, FontStyle.Bold);
    /// <summary>次级辅助说明/微标签/表头（8.5pt 常规）</summary>
    public static readonly Font FontCaption = new(PrimaryFontName, 8.5f);
    /// <summary>次级辅助说明/微标签/表头粗体（8.5pt 粗体）</summary>
    public static readonly Font FontCaptionBold = new(PrimaryFontName, 8.5f, FontStyle.Bold);
    /// <summary>等宽代码/机器数据字体（9pt 等宽）</summary>
    public static readonly Font FontMono = new(MonoFontName, 9.0f);

    /// <summary>菜单标准字体（13px / 9.75pt Bold 700 微软雅黑，对标 SwitchTask 饱满扎实字重）</summary>
    public static readonly Font FontMenu = new(MenuFontName, 9.75f, FontStyle.Bold);
    /// <summary>菜单常规字体（13px / 9.75pt 常规 微软雅黑）</summary>
    public static readonly Font FontMenuRegular = new(MenuFontName, 9.75f);
    /// <summary>菜单粗体（13px / 9.75pt 粗体 微软雅黑）</summary>
    public static readonly Font FontMenuBold = new(MenuFontName, 9.75f, FontStyle.Bold);
    /// <summary>菜单顶栏大标题（15px / 11.25pt Bold 700 微软雅黑，强化品牌视觉锚点）</summary>
    public static readonly Font FontMenuHeader = new(MenuFontName, 11.25f, FontStyle.Bold);

    /// <summary>大标题字体（关于窗口等强调用途；pt 尺寸随系统 DPI 由框架缩放）</summary>
    public static readonly Font FontTitle = new(PrimaryFontName, 17f, FontStyle.Bold);

    public static UiPalette Light { get; } = UiPalette.CreateLight();
    public static UiPalette Dark { get; } = UiPalette.CreateDark();

    public static UiPalette Current { get; private set; } = Light;
    public static bool IsDark => ReferenceEquals(Current, Dark);

    /// <summary>主题切换后触发（菜单等非窗口表面借此刷新）</summary>
    public static event Action? Changed;

    // ---- 颜色转发（兼容既有引用，绘制时实时取值）----
    public static Color WindowBg => Current.WindowBg;
    public static Color CardBg => Current.CardBg;
    public static Color CardBorder => Current.CardBorder;
    public static Color Accent => Current.Accent;
    public static Color AccentHover => Current.AccentHover;
    public static Color AccentDown => Current.AccentDown;
    public static Color AccentDisabled => Current.AccentDisabled;
    public static Color Text => Current.Text;
    public static Color Dim => Current.Dim;
    public static Color SectionTitle => Current.SectionTitle;
    public static Color ControlBorder => Current.ControlBorder;
    public static Color TrackBg => Current.TrackBg;
    public static Color ButtonFace => Current.ButtonFace;
    public static Color ButtonHover => Current.ButtonHover;
    public static Color ButtonDown => Current.ButtonDown;
    public static Color ButtonDisabledFace => Current.ButtonDisabledFace;
    public static Color CheckerEven => Current.CheckerEven;
    public static Color CheckerOdd => Current.CheckerOdd;

    // ---- DPI 工具（96 DPI 设计基线）----

    /// <summary>控件当前 DPI 相对设计基线（96）的缩放比。</summary>
    public static float DpiScale(Control? control) => (control?.DeviceDpi ?? 96) / DesignDpi;

    /// <summary>设计像素 → 控件当前 DPI 像素（自绘度量/像素常量用）。</summary>
    public static int Dpi(Control? control, int px) => (int)Math.Round(px * DpiScale(control));

    /// <summary>设计像素 → 控件当前 DPI 像素（浮点绘制坐标用）。</summary>
    public static float DpiF(Control? control, float px) => (float)(px * DpiScale(control));

    /// <summary>创建主题按钮（macOS 圆角风格）</summary>
    public static MacButton Button(string text, bool accent = false, int width = 110)
        => new(accent) { Text = text, Width = width, Height = 32, Font = Font };

    // ---- 主题应用 ----

    /// <summary>切换主题；对已打开窗口做调色板映射换肤并重绘。</summary>
    public static void Apply(bool dark)
    {
        UiPalette next = dark ? Dark : Light;
        if (ReferenceEquals(next, Current))
            return;
        UiPalette old = Current;
        Current = next;

        foreach (Form form in Application.OpenForms)
        {
            RemapTree(form, old, next);
            if (form is MacDialogForm macForm && macForm.IsHandleCreated)
                macForm.UpdateDwmTheme(dark);
            form.Invalidate(true);
        }

        Changed?.Invoke();
    }

    /// <summary>读取系统深浅色（高对比度下强制浅色）。</summary>
    public static bool IsSystemDark()
    {
        if (SystemInformation.HighContrast)
            return false;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>按系统设置检测并应用主题。</summary>
    public static void DetectAndApply() => Apply(IsSystemDark());

    /// <summary>参与换肤映射的调色板槽位（面色/边色/文字色；仅绘制用的槽位不参与，避免歧义）。</summary>
    private static readonly Func<UiPalette, Color>[] RemapFields =
    {
        p => p.CardBg,
        p => p.WindowBg,
        p => p.CardBorder,
        p => p.ControlBorder,
        p => p.TrackBg,
        p => p.Text,
        p => p.Dim,
        p => p.SectionTitle,
        p => p.Accent,
        p => p.AccentHover,
        p => p.AccentDown,
        p => p.AccentDisabled,
    };

    private static void RemapTree(Control control, UiPalette oldPalette, UiPalette newPalette)
    {
        Color bg = control.BackColor;
        Color mapped = MapColor(bg, oldPalette, newPalette);
        if (mapped != bg)
            control.BackColor = mapped;

        Color fg = control.ForeColor;
        mapped = MapColor(fg, oldPalette, newPalette);
        if (mapped != fg)
            control.ForeColor = mapped;

        foreach (Control child in control.Controls)
            RemapTree(child, oldPalette, newPalette);
    }

    private static Color MapColor(Color color, UiPalette oldPalette, UiPalette newPalette)
    {
        foreach (var get in RemapFields)
        {
            if (get(oldPalette) == color)
                return get(newPalette);
        }
        return color;
    }
}
