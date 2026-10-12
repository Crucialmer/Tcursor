namespace Tcursor.Core;

/// <summary>前台窗口的进程/标题/类名信息（数据载体）</summary>
internal readonly record struct WindowInfo(IntPtr Hwnd, uint Pid, string Process, string Title, string Class);
