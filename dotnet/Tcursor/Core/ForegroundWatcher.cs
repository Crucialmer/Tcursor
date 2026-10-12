using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Core;

/// <summary>
/// 前台窗口变化监听（SetWinEventHook, EVENT_SYSTEM_FOREGROUND）。
/// 事件在创建它的线程（UI 线程消息循环）回调，回调内只做唤醒动作。
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Action _onChange;
    private readonly WinEventProc _proc; // 保持引用，防止委托被 GC
    private IntPtr _hook;

    public ForegroundWatcher(Action onChange)
    {
        _onChange = onChange;
        _proc = OnWinEvent;
    }

    public void Start()
    {
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            _onChange();
        }
        catch
        {
            // 回调中不允许抛出
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
