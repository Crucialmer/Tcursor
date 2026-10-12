namespace Tcursor.Core;

/// <summary>六种输入状态：CN / EN / Caps / US / JP / KR</summary>
public enum InputState
{
    /// <summary>中文状态（中文键盘）</summary>
    Chinese,
    /// <summary>英文状态（中文键盘）</summary>
    English,
    /// <summary>大写锁定</summary>
    CapsLock,
    /// <summary>英文键盘（非中日韩布局）</summary>
    EnglishKeyboard,
    /// <summary>日文键盘</summary>
    Japanese,
    /// <summary>韩文键盘</summary>
    Korean,
}
