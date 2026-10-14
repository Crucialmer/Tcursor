using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Tcursor.Capture;
using Tcursor.Config;
using Tcursor.Core;
using Tcursor.Render;
using Tcursor.Ui;

namespace Tcursor;

/// <summary>
/// 自检模式：不打开界面，输出环境探测 + 渲染各种符号样例 PNG（供无头环境/CI 验证）。
/// 用法：Tcursor.exe --selftest &lt;输出目录&gt;
/// </summary>
internal static class Selftest
{
    public static int Run(string outDir)
    {
        var sb = new StringBuilder();
        try
        {
            Directory.CreateDirectory(outDir);
            sb.AppendLine("Tcursor selftest @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Version: " + AppInfo.Version);
            sb.AppendLine("Framework: " + RuntimeInformation.FrameworkDescription);
            sb.AppendLine("OS: " + RuntimeInformation.OSDescription);
            UiTheme.DetectAndApply();
            sb.AppendLine("Theme: " + (UiTheme.IsDark ? "Dark" : "Light"));

            bool capsLock = (Win32.NativeMethods.GetKeyState(Win32.NativeMethods.VK_CAPITAL) & 0x1) != 0;
            sb.AppendLine("CapsLock: " + capsLock);

            IntPtr foreground = Win32.NativeMethods.GetForegroundWindow();
            sb.AppendLine("ForegroundWindow: 0x" + foreground.ToString("X"));
            sb.AppendLine("IdleMs: " + SysInfo.GetIdleMs());
            sb.AppendLine("State(foreground): " + StateDetector.Detect(foreground));
            sb.AppendLine("Update: 当前版本 " + AppInfo.Version + "；更新源 " + UpdateService.Feed
                          + "；自检不联网（用 --check-update <文件> 做真实检查）");

            // 渲染配置中六种状态的符号
            AppConfig config = ConfigStore.Load();
            foreach (var (key, style) in config.Styles)
            {
                using Bitmap? bmp = SymbolRenderer.Render(style, config.ShapeCornerPreference, config.ShapeEdgeStyle, 1.0);
                if (bmp is null)
                {
                    sb.AppendLine($"render {key}: (null)");
                    continue;
                }
                string path = Path.Combine(outDir, $"style-{key}.png");
                bmp.Save(path, ImageFormat.Png);
                sb.AppendLine($"render {key}: {bmp.Width}x{bmp.Height} -> {Path.GetFileName(path)}");
            }

            // 形状样例（仅矩形：macOS 圆角档位 / macOS 边框样式 / 小尺寸平滑度）
            var samples = new (string Name, SymbolStyle Style, int Corner, int Edge)[]
            {
                ("corner-sharp-24", new SymbolStyle { ShapeColor = "#FF5757", ShapeWidth = 24, ShapeHeight = 24 }, 0, 0),
                ("corner-small-24", new SymbolStyle { ShapeColor = "#FF5757", ShapeWidth = 24, ShapeHeight = 24 }, 1, 0),
                ("corner-medium-24", new SymbolStyle { ShapeColor = "#FF5757", ShapeWidth = 24, ShapeHeight = 24 }, 2, 0),
                ("corner-large-24", new SymbolStyle { ShapeColor = "#FF5757", ShapeWidth = 24, ShapeHeight = 24 }, 3, 0),
                ("corner-xlarge-24", new SymbolStyle { ShapeColor = "#FF5757", ShapeWidth = 24, ShapeHeight = 24 }, 4, 0),
                ("corner-xxlarge-24", new SymbolStyle { ShapeColor = "#FF5757", ShapeWidth = 24, ShapeHeight = 24 }, 5, 0),
                ("corner-capsule-24", new SymbolStyle { ShapeColor = "#FF5757", ShapeWidth = 24, ShapeHeight = 24 }, 6, 0),
                ("corner-medium-12", new SymbolStyle { ShapeColor = "#1E88FF", ShapeWidth = 12, ShapeHeight = 12 }, 2, 0),
                ("corner-medium-48", new SymbolStyle { ShapeColor = "#FB8C00", ShapeWidth = 48, ShapeHeight = 48 }, 2, 0),
                ("edge-hairline-48", new SymbolStyle { ShapeColor = "#FB8C00", ShapeWidth = 48, ShapeHeight = 48 }, 2, 1),
                ("edge-gloss-48", new SymbolStyle { ShapeColor = "#FB8C00", ShapeWidth = 48, ShapeHeight = 48 }, 2, 2),
                ("edge-bevel-48", new SymbolStyle { ShapeColor = "#FB8C00", ShapeWidth = 48, ShapeHeight = 48 }, 2, 3),
                ("edge-shadow-48", new SymbolStyle { ShapeColor = "#34C759", ShapeWidth = 48, ShapeHeight = 48 }, 2, 4),
                ("edge-shadow-96", new SymbolStyle { ShapeColor = "#34C759", ShapeWidth = 96, ShapeHeight = 96 }, 2, 4),
            };
            var renderedSamples = new List<(string Name, Bitmap Bmp)>();
            foreach (var (name, style, corner, edge) in samples)
            {
                using Bitmap? bmp = SymbolRenderer.Render(style, corner, edge, 1.0);
                if (bmp is null)
                {
                    sb.AppendLine($"render {name}: (null)");
                    continue;
                }
                bmp.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                renderedSamples.Add((name, (Bitmap)bmp.Clone()));
                sb.AppendLine($"render {name}: {bmp.Width}x{bmp.Height} -> {name}.png");
            }

            // 形状总览图：上排放大（每项自适应缩放，上限 4x）、下排 1x 原尺寸（透明底）
            if (renderedSamples.Count > 0)
            {
                const int gap = 18;
                const int pad = 14;
                const int rowGap = 24;
                var disp = renderedSamples
                    .Select(s =>
                    {
                        double sc = Math.Min(4.0, 128.0 / Math.Max(s.Bmp.Width, s.Bmp.Height));
                        return (W: Math.Max(1, (int)Math.Round(s.Bmp.Width * sc)), H: Math.Max(1, (int)Math.Round(s.Bmp.Height * sc)));
                    })
                    .ToArray();
                int row1H = disp.Max(d => d.H);
                int row2H = renderedSamples.Max(s => s.Bmp.Height);
                int contentW = 0;
                for (int i = 0; i < disp.Length; i++)
                    contentW += Math.Max(disp[i].W, renderedSamples[i].Bmp.Width) + (i > 0 ? gap : 0);
                using var sheet = new Bitmap(pad * 2 + contentW, pad * 2 + row1H + rowGap + row2H, PixelFormat.Format32bppPArgb);
                using (var gs = Graphics.FromImage(sheet))
                {
                    int x = pad;
                    for (int i = 0; i < renderedSamples.Count; i++)
                    {
                        Bitmap bmp = renderedSamples[i].Bmp;
                        gs.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        gs.DrawImage(bmp, new Rectangle(x, pad + (row1H - disp[i].H) / 2, disp[i].W, disp[i].H));
                        x += Math.Max(disp[i].W, bmp.Width) + gap;
                    }
                    x = pad;
                    int y2 = pad + row1H + rowGap;
                    for (int i = 0; i < renderedSamples.Count; i++)
                    {
                        Bitmap bmp = renderedSamples[i].Bmp;
                        gs.InterpolationMode = InterpolationMode.NearestNeighbor;
                        gs.PixelOffsetMode = PixelOffsetMode.Half;
                        gs.DrawImage(bmp, new Rectangle(x, y2 + (row2H - bmp.Height) / 2, bmp.Width, bmp.Height));
                        x += Math.Max(disp[i].W, bmp.Width) + gap;
                    }
                }
                sheet.Save(Path.Combine(outDir, "shapes-overview.png"), ImageFormat.Png);
                sb.AppendLine($"render overview: shapes-overview.png ({renderedSamples.Count} 样例)");
                foreach (var (_, bmp) in renderedSamples)
                    bmp.Dispose();
            }

            // 托盘图标（内嵌应用图标资源）
            try
            {
                using Bitmap? trayRun = TrayIconFactory.CreateBitmap(false, 64);
                using Bitmap? trayPaused = TrayIconFactory.CreateBitmap(true, 64);
                bool trayOk = trayRun is not null && trayPaused is not null;
                if (trayRun is not null)
                    trayRun.Save(Path.Combine(outDir, "tray-running.png"), ImageFormat.Png);
                if (trayPaused is not null)
                    trayPaused.Save(Path.Combine(outDir, "tray-paused.png"), ImageFormat.Png);
                sb.AppendLine(trayOk ? "tray-icon: OK -> tray-running.png / tray-paused.png" : "tray-icon: FAIL（内嵌资源缺失）");
            }
            catch (Exception ex)
            {
                sb.AppendLine("tray-icon: FAIL " + ex.Message);
            }

            // 捕获链探测（无前台窗口时失败属预期）
            using (var chain = new CaptureChain())
            {
                if (foreground != IntPtr.Zero)
                {
                    Win32.NativeMethods.GetWindowThreadProcessId(foreground, out uint pid);
                    foreach (string mode in new[] { "GUI", "UIA", "MSAA" })
                    {
                        bool single = chain.TryCapture(pid, foreground, mode, out CaretRect singleRect);
                        sb.AppendLine($"capture({mode}): " + (single ? $"OK {singleRect}" : "失败（该后端不适用/无光标，属预期）"));
                    }
                    chain.Reset();
                    bool ok = chain.TryCapture(pid, foreground, "GUI>UIA>MSAA", out CaretRect rect);
                    sb.AppendLine(ok
                        ? $"capture(chain): OK via {chain.LastUsedMode} {rect}"
                        : "capture(chain): 失败（当前无光标焦点，属预期）");
                }
                else
                {
                    sb.AppendLine("capture: 无前台窗口（断开会话属预期）");
                }
            }

            // 分层窗口渲染路径（含位图/DC 缓存：首次 → 仅移动快速路径 → 换位图重建）
            try
            {
                using var layer = new LayeredSymbolWindow();
                using (var bmp1 = new Bitmap(24, 24, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(bmp1))
                        g.FillEllipse(Brushes.Red, 1, 1, 22, 22);
                    layer.ShowBitmap(bmp1, -4000, -4000);
                    layer.ShowBitmap(bmp1, -4000, -3980);
                }
                using (var bmp2 = new Bitmap(30, 30, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(bmp2))
                        g.FillRectangle(Brushes.Blue, 0, 0, 29, 29);
                    layer.ShowBitmap(bmp2, -4000, -3960);
                }
                layer.HideSymbol();
                sb.AppendLine("layer-window: OK");
            }
            catch (Exception ex)
            {
                sb.AppendLine("layer-window: FAIL " + ex.Message);
            }

            // 窗口信息读取（以自身窗口为靶）
            try
            {
                using var probeHost = new Form
                {
                    Text = "Tcursor.InfoProbe",
                    ClientSize = new Size(200, 80),
                    ShowInTaskbar = false,
                };
                _ = probeHost.Handle;
                WindowProbeResult probe = WindowProbe.GetInfo(probeHost.Handle);
                bool probeOk = probe.Process.Length > 0
                    && probe.RootClass.Length > 0
                    && probe.Title == "Tcursor.InfoProbe";
                sb.AppendLine((probeOk ? "window-info: OK " : "window-info: FAIL ")
                    + $"(proc={probe.Process}, class={probe.RootClass}, title={probe.Title})");
            }
            catch (Exception ex)
            {
                sb.AppendLine("window-info: FAIL " + ex.Message);
            }

            sb.AppendLine("RESULT: OK");
            File.WriteAllText(Path.Combine(outDir, "selftest.txt"), sb.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            sb.AppendLine("RESULT: FAIL");
            sb.AppendLine(ex.ToString());
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "selftest.txt"), sb.ToString());
            }
            catch
            {
                // 忽略
            }
            return 1;
        }
    }
}
