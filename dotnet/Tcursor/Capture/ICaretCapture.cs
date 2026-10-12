using Tcursor.Core;

namespace Tcursor.Capture;

/// <summary>光标捕获后端接口</summary>
internal interface ICaretCapture
{
    /// <summary>尝试获取光标位置；失败返回 false（不要抛异常）</summary>
    bool TryGetCaret(IntPtr hwnd, out CaretRect caret);
}
