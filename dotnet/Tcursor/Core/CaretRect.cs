namespace Tcursor.Core;

/// <summary>光标位置（屏幕物理像素坐标）</summary>
internal readonly record struct CaretRect(int Left, int Top, int Right, int Bottom)
{
}
