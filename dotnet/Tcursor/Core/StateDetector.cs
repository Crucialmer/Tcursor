using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Core;

/// <summary>
/// 输入法状态检测（六态）。
/// 流程：CapsLock 优先 → 键盘布局（JP/KR/US/中文）→ 中文布局查询 IME 打开状态/转换码。
/// </summary>
internal static class StateDetector
{
    private const ushort LangChinese = 0x04;
    private const ushort LangJapanese = 0x11;
    private const ushort LangKorean = 0x12;

    private const int IMC_GETCONVERSIONMODE = 0x1;
    private const int IMC_GETOPENSTATUS = 0x5;

    /// <summary>同一前台窗口内 IME 状态查询的最短间隔：查询要跨进程 SendMessageTimeout，
    /// 而 20ms 轮询意味着键入期每秒最多 50 次。状态切换（Shift）到符号更新允许 ≤40ms 的示意延迟，
    /// 因此按窗口 + 时间封顶，其余轮次复用上次结果。</summary>
    private const long ImeQueryMinIntervalMs = 40;

    private static IntPtr _imeCacheWnd;
    private static long _imeCacheAt;
    private static InputState _imeCacheState;

    public static InputState Detect(IntPtr fgWindow)
    {
        // 1) 大写锁定优先。
        // 注意：必须用 GetKeyState 的低位（切换态，持续有效）。
        // GetAsyncKeyState 的低位是“自上次读取后是否按下过”的一次性脉冲——轮询里读到即清，
        // 会导致大写状态几乎检测不到（且按下“关闭大写”时同样置位）。
        if ((GetKeyState(VK_CAPITAL) & 0x1) != 0)
            return InputState.CapsLock;

        // 2) 键盘布局
        ushort langId = 0;
        if (fgWindow != IntPtr.Zero)
        {
            uint tid = GetWindowThreadProcessId(fgWindow, out _);
            IntPtr hkl = GetKeyboardLayout(tid);
            langId = (ushort)((ulong)hkl & 0xFFFF);
        }

        ushort primary = (ushort)(langId & 0x3FF);

        if (primary == LangJapanese) return InputState.Japanese;
        if (primary == LangKorean) return InputState.Korean;
        if (primary != LangChinese) return InputState.EnglishKeyboard;

        // 3) 中文布局：查询 IME 状态（同窗口 40ms 内复用上次结果，见 ImeQueryMinIntervalMs）
        if (fgWindow == IntPtr.Zero)
            return InputState.English;

        long now = Environment.TickCount64;
        if (fgWindow == _imeCacheWnd && now - _imeCacheAt < ImeQueryMinIntervalMs)
            return _imeCacheState;

        InputState state = QueryChineseState(fgWindow);
        _imeCacheWnd = fgWindow;
        _imeCacheAt = now;
        _imeCacheState = state;
        return state;
    }

    /// <summary>中文布局下的实际查询：先读 IME 打开状态，失败再读转换码。</summary>
    private static InputState QueryChineseState(IntPtr fgWindow)
    {
        int? openStatus = QueryIme(fgWindow, IMC_GETOPENSTATUS);
        if (openStatus.HasValue)
            return openStatus.Value != 0 ? InputState.Chinese : InputState.English;

        int? conversion = QueryIme(fgWindow, IMC_GETCONVERSIONMODE);
        if (conversion.HasValue)
            return (conversion.Value & 0x1) != 0 ? InputState.Chinese : InputState.English;

        return InputState.English;
    }

    /// <summary>向 IME 窗口发送 WM_IME_CONTROL 查询（带超时，避免在无响应窗口上卡住）</summary>
    private static int? QueryIme(IntPtr fgWindow, int command)
    {
        try
        {
            IntPtr imeWnd = ImmGetDefaultIMEWnd(fgWindow);
            if (imeWnd == IntPtr.Zero)
                return null;

            IntPtr ret = SendMessageTimeout(imeWnd, WM_IME_CONTROL, (IntPtr)command, IntPtr.Zero,
                SMTO_ABORTIFHUNG, 200, out IntPtr result);
            if (ret == IntPtr.Zero)
                return null;
            return (int)result;
        }
        catch
        {
            return null;
        }
    }
}
