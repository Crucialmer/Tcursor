namespace Tcursor.Core;

/// <summary>光标位置（屏幕物理像素坐标）</summary>
internal readonly record struct CaretRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
