using System.Buffers;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Tcursor.Config;

namespace Tcursor.Render;

/// <summary>
/// 符号位图渲染（32bppPArgb，逐像素透明；仅矩形）。
/// 圆角为 Apple/Figma 连续曲率（corner smoothing = 0.6：贝塞尔肩 + 圆弧 + 贝塞尔肩，无曲率突变）；
/// 档位（相对短边）：0 直角 / 1 小 16% / 2 中 25% / 3 大 33%（默认）/ 4 特大 40% / 5 超大 45% / 6 胶囊 50%。
/// 边框样式（macOS 标准）：0 无 / 1 细描边 / 2 内高光 / 3 微立体（描边+高光）/ 4 柔和阴影。
/// 整幅按倍率超采样后高质量缩放；「柔和阴影」样式向四周扩出阴影所需边距（GetShadowPadding）。
/// </summary>
internal static class SymbolRenderer
{
    /// <summary>Apple 风格连续曲率平滑系数（Figma corner smoothing，≈ iOS 图标观感）</summary>
    private const float CornerSmoothing = 0.6f;

    private const int ShadowEdgeStyle = 4;

    /// <summary>圆角半径比例（相对短边）</summary>
    private static float RadiusRatio(int cornerPreference) => cornerPreference switch
    {
        1 => 0.16f,   // 小
        2 => 0.25f,   // 中
        3 => 0.33f,   // 大（默认）
        4 => 0.40f,   // 特大
        5 => 0.45f,   // 超大
        6 => 0.50f,   // 胶囊
        _ => 0f,      // 直角
    };

    /// <summary>「柔和阴影」样式所需的额外边距（逻辑像素，四周相同；控制器用于补偿显示位置）</summary>
    public static int GetShadowPadding(SymbolStyle style, int edgeStyle, double scale)
    {
        if (style.Kind != SymbolKind.Shape || edgeStyle != ShadowEdgeStyle)
            return 0;
        int w = Math.Max(2, (int)Math.Round(style.ShapeWidth * scale));
        int h = Math.Max(2, (int)Math.Round(style.ShapeHeight * scale));
        return ShadowPad(w, h);
    }

    private static int ShadowPad(int w, int h)
        => (int)Math.Ceiling(Math.Max(1.5f, Math.Min(w, h) * 0.13f)) + 2;

    /// <summary>渲染形状符号；cornerPreference / edgeStyle 来自全局配置</summary>
    public static Bitmap? Render(SymbolStyle style, int cornerPreference, int edgeStyle, double scale)
    {
        try
        {
            if (style.Kind != SymbolKind.Shape)
                return null;
            return RenderShape(style, cornerPreference, edgeStyle, scale);
        }
        catch (Exception ex)
        {
            Core.Log.Error(ex);
            return null;
        }
    }

    private static Bitmap RenderShape(SymbolStyle style, int cornerPreference, int edgeStyle, double scale)
    {
        int effectiveCorner = (style.CornerPreference >= 0 && style.CornerPreference <= 6)
            ? style.CornerPreference
            : cornerPreference;
        int w = Math.Max(2, (int)Math.Round(style.ShapeWidth * scale));
        int h = Math.Max(2, (int)Math.Round(style.ShapeHeight * scale));
        int alpha = Math.Clamp(style.Transparency, 0, 255);
        int pad = edgeStyle == ShadowEdgeStyle ? ShadowPad(w, h) : 0;

        var bmp = new Bitmap(w + pad * 2, h + pad * 2, PixelFormat.Format32bppPArgb);
        int ss = SupersampleFactor(bmp.Width, bmp.Height);
        if (ss <= 1)
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawSymbol(g, style, effectiveCorner, edgeStyle, alpha, w, h, pad, 1);
            return bmp;
        }

        using (var big = new Bitmap(bmp.Width * ss, bmp.Height * ss, PixelFormat.Format32bppPArgb))
        {
            using (var gb = Graphics.FromImage(big))
            {
                gb.SmoothingMode = SmoothingMode.AntiAlias;
                DrawSymbol(gb, style, effectiveCorner, edgeStyle, alpha, w, h, pad, ss);
            }
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(big, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, big.Width, big.Height, GraphicsUnit.Pixel);
        }
        return bmp;
    }

    /// <summary>超采样倍率（尺寸越大越低，控制内存与耗时）</summary>
    private static int SupersampleFactor(int w, int h)
    {
        int side = Math.Max(w, h);
        if (side <= 96) return 4;
        if (side <= 192) return 3;
        if (side <= 384) return 2;
        return 1;
    }

    /// <summary>在画布上绘制：阴影（可选）→ 形状填充 → 边框样式；ss = 画布单位（超采样倍率）</summary>
    private static void DrawSymbol(Graphics g, SymbolStyle style, int cornerPreference, int edgeStyle, int alpha, int w, int h, int pad, int ss)
    {
        float x0 = pad * ss;
        float y0 = pad * ss;
        float sw = w * ss;
        float sh = h * ss;
        float radius = Math.Min(RadiusRatio(cornerPreference) * Math.Min(sw, sh), Math.Min(sw, sh) / 2f);

        if (edgeStyle == ShadowEdgeStyle)
        {
            int cw = (w + pad * 2) * ss;
            int ch = (h + pad * 2) * ss;
            float blur = Math.Max(1.5f, Math.Min(sw, sh) * 0.13f);
            float shadowAlpha = 0.36f * (alpha / 255f);
            using var silhouette = SmoothRectPath(x0, y0, sw, sh, radius, CornerSmoothing);
            DrawSoftShadow(g, silhouette, cw, ch, blur, shadowAlpha, ss);
        }

        Color color = ApplyAlpha(ParseColor(style.ShapeColor, Color.Red), alpha);
        using (var brush = new SolidBrush(color))
        using (var fill = SmoothRectPath(x0, y0, sw, sh, radius, CornerSmoothing))
        {
            g.FillPath(brush, fill);
        }

        DrawEdgeStyle(g, x0, y0, sw, sh, radius, edgeStyle, alpha, ss);
    }

    /// <summary>macOS 标准边框样式：1 细描边 / 2 内高光 / 3 微立体 / 4 柔和阴影（暗边）</summary>
    private static void DrawEdgeStyle(Graphics g, float x, float y, float w, float h, float radius, int edgeStyle, int alpha, int unit)
    {
        if (edgeStyle <= 0 || w < 4 * unit || h < 4 * unit)
            return;

        float strokeAlpha = Math.Clamp(alpha, 60, 255) / 255f;
        float inset = unit * 0.5f;
        using var outline = SmoothRectPath(x + inset, y + inset, w - unit, h - unit, Math.Max(0f, radius - inset), CornerSmoothing);

        switch (edgeStyle)
        {
            case 1:
                // 细描边：发丝描边（macOS 边框标准，按符号尺寸适当加强以保证可辨）
                using (var pen = new Pen(Color.FromArgb((int)(64 * strokeAlpha), 0, 0, 0), unit))
                {
                    g.DrawPath(pen, outline);
                }
                break;

            case 2:
                // 内高光：顶部 1px 白色高光渐隐 + 底部 1px 暗边（macOS 按钮质感）
                DrawGloss(g, outline, x, y, w, h, strokeAlpha, unit);
                break;

            case 3:
                // 微立体 = 细描边 + 内高光
                using (var pen = new Pen(Color.FromArgb((int)(64 * strokeAlpha), 0, 0, 0), unit))
                {
                    g.DrawPath(pen, outline);
                }
                DrawGloss(g, outline, x, y, w, h, strokeAlpha, unit);
                break;

            case 4:
                // 柔和阴影（阴影在下层已绘制）：一圈微暗边让轮廓清晰（近似 macOS `0 0 3px rgba(0,0,0,.55)` 的收敛版）
                using (var pen = new Pen(Color.FromArgb((int)(70 * strokeAlpha), 0, 0, 0), unit))
                {
                    g.DrawPath(pen, outline);
                }
                break;
        }
    }

    private static void DrawGloss(Graphics g, GraphicsPath outline, float x, float y, float w, float h, float strokeAlpha, int unit)
    {
        var rect = new RectangleF(x, y, w, h);
        using (var top = new LinearGradientBrush(rect,
                   Color.FromArgb((int)(175 * strokeAlpha), 255, 255, 255),
                   Color.FromArgb(0, 255, 255, 255), 90f))
        using (var pen = new Pen(top, unit))
        {
            g.DrawPath(pen, outline);
        }
        using (var bottom = new LinearGradientBrush(rect,
                   Color.FromArgb(0, 0, 0, 0),
                   Color.FromArgb((int)(56 * strokeAlpha), 0, 0, 0), 90f))
        using (var pen = new Pen(bottom, unit))
        {
            g.DrawPath(pen, outline);
        }
    }

    // ---------------- 柔和阴影 ----------------

    private static void DrawSoftShadow(Graphics g, GraphicsPath silhouette, int canvasW, int canvasH, float blurPx, float alpha, int unit)
    {
        if (alpha <= 0.01f)
            return;
        using var mask = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);
        using (var gm = Graphics.FromImage(mask))
        {
            gm.SmoothingMode = SmoothingMode.AntiAlias;
            gm.TranslateTransform(0f, unit);   // 阴影向下偏移 1 逻辑像素（macOS 投影惯例）
            using var black = new SolidBrush(Color.Black);
            gm.FillPath(black, silhouette);
        }
        BlurAlpha(mask, (int)Math.Round(blurPx));
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
        g.DrawImage(mask, new Rectangle(0, 0, canvasW, canvasH), 0, 0, canvasW, canvasH, GraphicsUnit.Pixel, ia);
    }

    /// <summary>对位图 alpha 通道做两遍盒模糊（近似高斯），用于柔和阴影。
    /// 优化：通过 ArrayPool 共享内存池租借工作数组，实现零 GC 堆内存分配。</summary>
    private static void BlurAlpha(Bitmap bmp, int radius)
    {
        if (radius <= 0)
            return;
        int w = bmp.Width;
        int h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        int len = stride * h;
        int pixelCount = w * h;

        byte[] buf = ArrayPool<byte>.Shared.Rent(len);
        float[] a = ArrayPool<float>.Shared.Rent(pixelCount);
        float[] t = ArrayPool<float>.Shared.Rent(pixelCount);
        try
        {
            Marshal.Copy(data.Scan0, buf, 0, len);
            for (int yy = 0; yy < h; yy++)
            {
                int row = yy * stride;
                int arow = yy * w;
                for (int xx = 0; xx < w; xx++)
                    a[arow + xx] = buf[row + xx * 4 + 3];
            }
            int r = Math.Max(1, radius / 2);
            BoxPass(a, t, w, h, r);
            BoxPass(a, t, w, h, r);
            for (int yy = 0; yy < h; yy++)
            {
                int row = yy * stride;
                int arow = yy * w;
                for (int xx = 0; xx < w; xx++)
                {
                    int idx = row + xx * 4;
                    buf[idx] = 0;
                    buf[idx + 1] = 0;
                    buf[idx + 2] = 0;
                    buf[idx + 3] = (byte)Math.Clamp((int)Math.Round(a[arow + xx]), 0, 255);
                }
            }
            Marshal.Copy(buf, 0, data.Scan0, len);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
            ArrayPool<float>.Shared.Return(a);
            ArrayPool<float>.Shared.Return(t);
            bmp.UnlockBits(data);
        }
    }

    private static void BoxPass(float[] a, float[] t, int w, int h, int r)
    {
        // 水平
        for (int yy = 0; yy < h; yy++)
        {
            int row = yy * w;
            float sum = 0f;
            int cnt = 0;
            for (int k = -r; k <= r; k++)
            {
                if (k >= 0 && k < w)
                {
                    sum += a[row + k];
                    cnt++;
                }
            }
            for (int xx = 0; xx < w; xx++)
            {
                t[row + xx] = sum / cnt;
                int add = xx + r + 1;
                int rem = xx - r;
                if (add < w)
                {
                    sum += a[row + add];
                    cnt++;
                }
                if (rem >= 0)
                {
                    sum -= a[row + rem];
                    cnt--;
                }
            }
        }
        // 垂直
        for (int xx = 0; xx < w; xx++)
        {
            float sum = 0f;
            int cnt = 0;
            for (int k = -r; k <= r; k++)
            {
                if (k >= 0 && k < h)
                {
                    sum += t[k * w + xx];
                    cnt++;
                }
            }
            for (int yy = 0; yy < h; yy++)
            {
                a[yy * w + xx] = sum / cnt;
                int add = yy + r + 1;
                int rem = yy - r;
                if (add < h)
                {
                    sum += t[add * w + xx];
                    cnt++;
                }
                if (rem >= 0)
                {
                    sum -= t[rem * w + xx];
                    cnt--;
                }
            }
        }
    }

    // ---------------- 路径 ----------------

    /// <summary>
    /// Apple/Figma 连续曲率圆角矩形路径（corner smoothing：贝塞尔肩 + 中间圆弧 + 贝塞尔肩，无曲率突变）。
    /// 数学来自 Figma 博文与 MartinRGB/figma-squircle 的实现（p=(1+s)R、圆弧夹角 90(1-s)、G2 连续控制点）。
    /// smoothing=0 时退化为普通圆弧圆角。供符号渲染与界面控件共用。
    /// </summary>
    internal static GraphicsPath SmoothRectPath(float x, float y, float w, float h, float radius, float smoothing)
    {
        var path = new GraphicsPath();
        if (w <= 0.01f || h <= 0.01f)
            return path;
        float r = Math.Min(radius, Math.Min(w, h) / 2f);
        if (r <= 0.01f)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
            return path;
        }

        float budget = Math.Min(w, h) / 2f;
        float p = (1f + smoothing) * r;
        float maxSmoothing = budget / r - 1f;          // 空间不足时压缩平滑度（同 Figma 默认行为）
        smoothing = Math.Min(smoothing, Math.Max(0f, maxSmoothing));
        p = Math.Min(p, budget);

        float arcMeasure = 90f * (1f - smoothing);     // 圆弧夹角（度）
        float arcLen = (float)(Math.Sin(ToRad(arcMeasure / 2)) * r * Math.Sqrt(2));
        float angleAlpha = (90f - arcMeasure) / 2f;
        float p3p4 = (float)(r * Math.Tan(ToRad(angleAlpha / 2)));
        float angleBeta = 45f * smoothing;
        float c = (float)(p3p4 * Math.Cos(ToRad(angleBeta)));
        float d = (float)(c * Math.Tan(ToRad(angleBeta)));
        float b = (p - arcLen - c - d) / 3f;
        float a = 2f * b;

        float W = x + w;
        float H = y + h;

        // 起点：上边，距右上角 p
        var start = new PointF(W - p, y);
        // 右上角：贝塞尔肩 → 圆弧 → 贝塞尔肩
        var p1 = new PointF(W - p + a + b + c, y + d);
        path.AddBezier(start, new PointF(start.X + a, y), new PointF(start.X + a + b, y), p1);
        AddCornerArc(path, W - r, y + r, r, p1, arcMeasure);
        var p2 = new PointF(p1.X + arcLen, p1.Y + arcLen);
        path.AddBezier(p2, new PointF(p2.X + d, p2.Y + c), new PointF(p2.X + d, p2.Y + b + c), new PointF(p2.X + d, p2.Y + a + b + c));

        // 右下角
        var r1 = new PointF(W - d, H - p + a + b + c);
        path.AddBezier(new PointF(W, H - p), new PointF(W, H - p + a), new PointF(W, H - p + a + b), r1);
        AddCornerArc(path, W - r, H - r, r, r1, arcMeasure);
        var r2 = new PointF(r1.X - arcLen, r1.Y + arcLen);
        path.AddBezier(r2, new PointF(r2.X - c, r2.Y + d), new PointF(r2.X - (b + c), r2.Y + d), new PointF(r2.X - (a + b + c), r2.Y + d));

        // 左下角
        var t1 = new PointF(x + p - (a + b + c), H - d);
        path.AddBezier(new PointF(x + p, H), new PointF(x + p - a, H), new PointF(x + p - (a + b), H), t1);
        AddCornerArc(path, x + r, H - r, r, t1, arcMeasure);
        var t2 = new PointF(t1.X - arcLen, t1.Y - arcLen);
        path.AddBezier(t2, new PointF(t2.X - d, t2.Y - c), new PointF(t2.X - d, t2.Y - (b + c)), new PointF(t2.X - d, t2.Y - (a + b + c)));

        // 左上角
        var l1 = new PointF(x + d, y + p - (a + b + c));
        path.AddBezier(new PointF(x, y + p), new PointF(x, y + p - a), new PointF(x, y + p - (a + b)), l1);
        AddCornerArc(path, x + r, y + r, r, l1, arcMeasure);
        var l2 = new PointF(l1.X + arcLen, l1.Y - arcLen);
        path.AddBezier(l2, new PointF(l2.X + c, l2.Y - d), new PointF(l2.X + b + c, l2.Y - d), new PointF(l2.X + a + b + c, l2.Y - d));

        path.CloseFigure();
        return path;
    }

    private static void AddCornerArc(GraphicsPath path, float cx, float cy, float r, PointF from, float sweepDeg)
    {
        float startDeg = (float)(Math.Atan2(from.Y - cy, from.X - cx) * 180.0 / Math.PI);
        path.AddArc(cx - r, cy - r, r * 2f, r * 2f, startDeg, sweepDeg);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    // ---------------- 颜色工具 ----------------

    internal static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        string s = value.Trim().TrimStart('#');
        try
        {
            if (s.Length == 8)
            {
                uint v = uint.Parse(s, System.Globalization.NumberStyles.HexNumber);
                return Color.FromArgb(
                    (int)(v >> 24), (int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
            }
            if (s.Length == 6)
            {
                int v = int.Parse(s, System.Globalization.NumberStyles.HexNumber);
                return Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            }
        }
        catch
        {
            // 解析失败使用兜底色
        }
        return fallback;
    }

    private static Color ApplyAlpha(Color color, int transparency)
        => Color.FromArgb(Math.Clamp(color.A * Math.Clamp(transparency, 0, 255) / 255, 0, 255), color.R, color.G, color.B);
}
