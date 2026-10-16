using System.Drawing;
using Microsoft.Win32;

namespace Tcursor.Ui;

/// <summary>一套界面调色板（浅色/深色共用同一组语义槽位）</summary>
internal sealed class UiPalette
{
    // 基础面
    public Color WindowBg;
    public Color CardBg;
    public Color SubCardBg;
    public Color CardBorder;
    public Color CardHighlight;
    public Color Accent;
    public Color AccentHover;
    public Color AccentDown;
    public Color AccentDisabled;
    public Color Text;
    public Color Dim;
    public Color SectionTitle;
    public Color ControlBorder;
    public Color TrackBg;
    public Color SuccessGreen;
    public Color WarningOrange;

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
    public Color TrafficRedBorder;
    public Color TrafficYellow;
    public Color TrafficYellowBorder;
    public Color TrafficGreen;
    public Color TrafficGreenBorder;
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
        SubCardBg = Color.FromArgb(0xF8, 0xF8, 0xFA),
        CardBorder = Color.FromArgb(0xE2, 0xE2, 0xE8),
        CardHighlight = Color.FromArgb(180, 255, 255, 255),
        Accent = Color.FromArgb(0x00, 0x7A, 0xFF),
        AccentHover = Color.FromArgb(0x26, 0x8C, 0xFF),
        AccentDown = Color.FromArgb(0x00, 0x62, 0xCC),
        AccentDisabled = Color.FromArgb(0xED, 0xED, 0xF0),
        Text = Color.FromArgb(0x1D, 0x1D, 0x1F),
        Dim = Color.FromArgb(0x86, 0x86, 0x8B),
        SectionTitle = Color.FromArgb(0x48, 0x48, 0x4A),
        ControlBorder = Color.FromArgb(0xD1, 0xD1, 0xD6),
        TrackBg = Color.FromArgb(0xE5, 0xE5, 0xEA),
        SuccessGreen = Color.FromArgb(0x34, 0xC7, 0x59),
        WarningOrange = Color.FromArgb(0xFF, 0x95, 0x00),
        ButtonFace = Color.White,
        ButtonHover = Color.FromArgb(0xFA, 0xFA, 0xFC),
        ButtonDown = Color.FromArgb(0xE5, 0xE5, 0xEA),
        ButtonDisabledFace = Color.FromArgb(0xF5, 0xF5, 0xF7),
        CheckerEven = Color.White,
        CheckerOdd = Color.FromArgb(0xF0, 0xF0, 0xF3),
        HeaderTitle = Color.FromArgb(0x3C, 0x3C, 0x43),
        HeaderTitleInactive = Color.FromArgb(0x8E, 0x8E, 0x93),
        HeaderLine = Color.FromArgb(0xE5, 0xE5, 0xEA),
        TrafficRed = Color.FromArgb(0xFF, 0x5F, 0x56),
        TrafficRedBorder = Color.FromArgb(0xE0, 0x44, 0x3E),
        TrafficYellow = Color.FromArgb(0xFF, 0xBD, 0x2E),
        TrafficYellowBorder = Color.FromArgb(0xDE, 0xA1, 0x23),
        TrafficGreen = Color.FromArgb(0x27, 0xC9, 0x3F),
        TrafficGreenBorder = Color.FromArgb(0x1A, 0xAB, 0x29),
        TrafficInactive = Color.FromArgb(0xD6, 0xD6, 0xDA),
        TrafficGlyph = Color.FromArgb(0x4D, 0x00, 0x00),
        MenuBack = Color.FromArgb(0xFA, 0xFA, 0xFC),
        MenuBorder = Color.FromArgb(0xDC, 0xDC, 0xE2),
        MenuText = Color.FromArgb(0x1D, 0x1D, 0x1F),
        MenuHighlightBack = Color.FromArgb(0x00, 0x7A, 0xFF),
        MenuHighlightText = Color.White,
    };

    public static UiPalette CreateDark() => new()
    {
        WindowBg = Color.FromArgb(0x18, 0x18, 0x1B),
        CardBg = Color.FromArgb(0x24, 0x24, 0x28),
        SubCardBg = Color.FromArgb(0x1E, 0x1E, 0x22),
        CardBorder = Color.FromArgb(0x36, 0x36, 0x3E),
        CardHighlight = Color.FromArgb(28, 255, 255, 255),
        Accent = Color.FromArgb(0x0A, 0x84, 0xFF),
        AccentHover = Color.FromArgb(0x33, 0x95, 0xFF),
        AccentDown = Color.FromArgb(0x00, 0x66, 0xD6),
        AccentDisabled = Color.FromArgb(0x36, 0x36, 0x3C),
        Text = Color.FromArgb(0xF2, 0xF2, 0xF7),
        Dim = Color.FromArgb(0x98, 0x98, 0x9D),
        SectionTitle = Color.FromArgb(0xD8, 0xD8, 0xDE),
        ControlBorder = Color.FromArgb(0x44, 0x44, 0x4A),
        TrackBg = Color.FromArgb(0x34, 0x34, 0x38),
        SuccessGreen = Color.FromArgb(0x30, 0xD1, 0x58),
        WarningOrange = Color.FromArgb(0xFF, 0x9F, 0x0A),
        ButtonFace = Color.FromArgb(0x32, 0x32, 0x36),
        ButtonHover = Color.FromArgb(0x3E, 0x3E, 0x44),
        ButtonDown = Color.FromArgb(0x4A, 0x4A, 0x50),
        ButtonDisabledFace = Color.FromArgb(0x24, 0x24, 0x28),
        CheckerEven = Color.FromArgb(0x28, 0x28, 0x2C),
        CheckerOdd = Color.FromArgb(0x20, 0x20, 0x24),
        HeaderTitle = Color.FromArgb(0xF2, 0xF2, 0xF7),
        HeaderTitleInactive = Color.FromArgb(0x6E, 0x6E, 0x73),
        HeaderLine = Color.FromArgb(0x32, 0x32, 0x38),
        TrafficRed = Color.FromArgb(0xFF, 0x5F, 0x56),
        TrafficRedBorder = Color.FromArgb(0xE0, 0x44, 0x3E),
        TrafficYellow = Color.FromArgb(0xFF, 0xBD, 0x2E),
        TrafficYellowBorder = Color.FromArgb(0xDE, 0xA1, 0x23),
        TrafficGreen = Color.FromArgb(0x27, 0xC9, 0x3F),
        TrafficGreenBorder = Color.FromArgb(0x1A, 0xAB, 0x29),
        TrafficInactive = Color.FromArgb(0x44, 0x44, 0x48),
        TrafficGlyph = Color.FromArgb(0x30, 0x00, 0x00),
        MenuBack = Color.FromArgb(0x20, 0x20, 0x24),
        MenuBorder = Color.FromArgb(0x36, 0x36, 0x3E),
        MenuText = Color.FromArgb(0xF2, 0xF2, 0xF7),
        MenuHighlightBack = Color.FromArgb(42, 255, 255, 255),
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
        string[] candidates = { "Microsoft YaHei UI", "PingFang SC", "Segoe UI Variable Text", "Segoe UI" };
        foreach (var name in candidates)
        {
            try
            {
                using var test = new FontFamily(name);
                return name;
            }
            catch { }
        }
        return "Microsoft YaHei UI";
    }

    private static string ResolveMenuFontFamily()
    {
        string[] candidates = { "Microsoft YaHei UI", "Segoe UI Variable Text", "PingFang SC", "Segoe UI" };
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
        string[] candidates = { "Cascadia Code", "Cascadia Mono", "Consolas", "Courier New" };
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

    /// <summary>主正文字体（10.5pt 常规，舒展大气）</summary>
    public static readonly Font Font = new(PrimaryFontName, 10.5f);
    /// <summary>主粗体（10.5pt 粗体）</summary>
    public static readonly Font FontBold = new(PrimaryFontName, 10.5f, FontStyle.Bold);
    /// <summary>分段卡片标题（12pt 粗体，强化结构层级）</summary>
    public static readonly Font FontSectionTitle = new(PrimaryFontName, 12.0f, FontStyle.Bold);
    /// <summary>次级辅助说明/微标签/表头（9.5pt 常规）</summary>
    public static readonly Font FontCaption = new(PrimaryFontName, 9.5f);
    /// <summary>次级辅助说明/微标签/表头粗体（9.5pt 粗体）</summary>
    public static readonly Font FontCaptionBold = new(PrimaryFontName, 9.5f, FontStyle.Bold);
    /// <summary>等宽代码/机器数据字体（10pt 等宽）</summary>
    public static readonly Font FontMono = new(MonoFontName, 10.0f);

    /// <summary>菜单标准字体（10.5pt 常规，典雅舒朗，彻底消除过重黑体拥挤感）</summary>
    public static readonly Font FontMenu = new(MenuFontName, 10.5f);
    /// <summary>菜单常规字体（10.5pt 常规）</summary>
    public static readonly Font FontMenuRegular = new(MenuFontName, 10.5f);
    /// <summary>菜单粗体（10.5pt 粗体）</summary>
    public static readonly Font FontMenuBold = new(MenuFontName, 10.5f, FontStyle.Bold);
    /// <summary>菜单顶栏大标题（12.5pt Bold 700，强化品牌视觉锚点）</summary>
    public static readonly Font FontMenuHeader = new(MenuFontName, 12.5f, FontStyle.Bold);

    /// <summary>窗体大标题（16pt 粗体）</summary>
    public static readonly Font FontTitle = new(PrimaryFontName, 16f, FontStyle.Bold);
    /// <summary>超大展示标题（关于窗口等，22pt 粗体）</summary>
    public static readonly Font FontHero = new(PrimaryFontName, 22f, FontStyle.Bold);

    public static UiPalette Light { get; } = UiPalette.CreateLight();
    public static UiPalette Dark { get; } = UiPalette.CreateDark();

    public static UiPalette Current { get; private set; } = Light;
    public static bool IsDark => ReferenceEquals(Current, Dark);

    /// <summary>主题切换后触发（菜单等非窗口表面借此刷新）</summary>
    public static event Action? Changed;

    // ---- 颜色转发（兼容既有引用，绘制时实时取值）----
    public static Color WindowBg => Current.WindowBg;
    public static Color CardBg => Current.CardBg;
    public static Color SubCardBg => Current.SubCardBg;
    public static Color CardBorder => Current.CardBorder;
    public static Color CardHighlight => Current.CardHighlight;
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

    // ---- DPI 工具（96 DPI 设计基线，完美支持高达 8K / 500% 缩放）----

    private static int GetPrimaryDpi()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return (int)g.DpiX;
        }
        catch
        {
            return 96;
        }
    }

    /// <summary>控件当前 DPI 相对设计基线（96）的缩放比。未关联窗口或未创建句柄时查询主屏物理 DPI（适配 8K 高清冷启动）。</summary>
    public static float DpiScale(Control? control)
    {
        int dpi = control is { IsHandleCreated: true, DeviceDpi: > 0 } ? control.DeviceDpi : GetPrimaryDpi();
        return dpi / DesignDpi;
    }

    /// <summary>设计像素 → 控件当前 DPI 像素（自绘度量/像素常量用）。</summary>
    public static int Dpi(Control? control, int px) => (int)Math.Round(px * DpiScale(control));

    /// <summary>设计像素 → 控件当前 DPI 像素（浮点绘制坐标用）。</summary>
    public static float DpiF(Control? control, float px) => (float)(px * DpiScale(control));

    /// <summary>创建主题按钮（macOS 26 连续曲率圆角风格）</summary>
    public static MacButton Button(string text, bool accent = false, int width = 110)
        => new(accent) { Text = text, Width = width, Height = 34, Font = Font };

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
