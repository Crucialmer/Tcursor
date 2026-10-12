using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace Tcursor.Ui;

/// <summary>托盘/应用图标工厂：内嵌应用图标资源（运行 = 蓝、暂停 = 灰）</summary>
internal static class TrayIconFactory
{
    private static Stream? OpenAsset(string fileName)
    {
        Assembly asm = typeof(TrayIconFactory).Assembly;
        string suffix = "Assets." + fileName;
        foreach (string name in asm.GetManifestResourceNames())
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return asm.GetManifestResourceStream(name);
        }
        return null;
    }

    /// <summary>生成位图（用于托盘图标与自检导出）；size 省略时按系统小图标尺寸（随 DPI）</summary>
    public static Bitmap? CreateBitmap(bool paused, int size = 0)
    {
        if (size <= 0)
            size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        try
        {
            using Stream? s = OpenAsset(paused ? "default-app-paused.png" : "default-app.png");
            if (s is null)
                return null;
            using var source = new Bitmap(s);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, size, size));
            return bmp;
        }
        catch (Exception ex)
        {
            Core.Log.Error(ex);
            return null;
        }
    }

    /// <summary>创建托盘图标（资源缺失时回退系统图标副本）；返回值归调用方所有，可安全 Dispose</summary>
    public static Icon Create(bool paused)
    {
        Bitmap? bmp = CreateBitmap(paused);
        if (bmp is null)
            return (Icon)SystemIcons.Application.Clone();
        try
        {
            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(hIcon);
                return (Icon)tmp.Clone();
            }
            finally
            {
                Win32.NativeMethods.DestroyIcon(hIcon);
            }
        }
        catch
        {
            return (Icon)SystemIcons.Application.Clone();
        }
        finally
        {
            bmp.Dispose();
        }
    }
}
