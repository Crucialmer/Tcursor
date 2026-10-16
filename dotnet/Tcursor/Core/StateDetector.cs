using System.Runtime.InteropServices;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Core;

/// <summary>
/// 输入法状态检测（六态）。
/// 流程：CapsLock 优先 → 键盘布局（JP/KR/US/中文）→ 中文布局查询 IME 打开状态/转换码（IMM32 + TSF 全局状态仓双通道）。
/// </summary>
internal static class StateDetector
{
    private const ushort LangChinese = 0x04;
    private const ushort LangJapanese = 0x11;
    private const ushort LangKorean = 0x12;

    private const int IMC_GETCONVERSIONMODE = 0x1;
    private const int IMC_GETOPENSTATUS = 0x5;

    // TSF 键盘状态仓 GUID
    private static readonly Guid GuidCompartmentKeyboardOpenClose = new("58273aad-01bb-4164-95c6-755ba0b5162d");
    private static readonly Guid GuidCompartmentKeyboardConversion = new("ccf05dd8-4a87-11d7-a6e2-00065b84435c");

    /// <summary>同一前台窗口内 IME 状态查询的最短间隔：查询要跨进程 SendMessageTimeout，
    /// 而 20ms 轮询意味着键入期每秒最多 50 次。状态切换（Shift）到符号更新允许 ≤40ms 的示意延迟，
    /// 因此按窗口 + 时间封顶，其余轮次复用上次结果。</summary>
    private const long ImeQueryMinIntervalMs = 40;

    private static IntPtr _imeCacheWnd;
    private static long _imeCacheAt;
    private static InputState _imeCacheState;

    // 辅助状态记录（应对高权限应用 UIPI 阻断跨进程消息的场景，如以管理员运行的 Quicker）
    private static InputState? _lastValidChineseSubState;
    private static bool _shiftWasDown;
    private static long _shiftDownAt;

    public static InputState Detect(IntPtr fgWindow)
    {
        // 1) 大写锁定优先。
        // 注意：必须用 GetKeyState 的低位（切换态，持续有效）。
        // GetAsyncKeyState 的低位是“自上次读取后是否按下过”的一次性脉冲——轮询里读到即清，
        // 会导致大写状态几乎检测不到（且按下“关闭大写”时同样置位）。
        if ((GetKeyState(VK_CAPITAL) & 0x1) != 0)
            return InputState.CapsLock;

        // 优先使用当前前台 GUI 线程的键盘焦点窗口（hwndFocus），无焦点窗口时回退到顶层窗口
        IntPtr targetWnd = fgWindow;
        uint tid = 0;
        if (fgWindow != IntPtr.Zero)
            tid = GetWindowThreadProcessId(fgWindow, out _);

        var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(0, ref gti))
        {
            if (gti.hwndFocus != IntPtr.Zero)
            {
                targetWnd = gti.hwndFocus;
                tid = GetWindowThreadProcessId(targetWnd, out _);
            }
            else if (gti.hwndActive != IntPtr.Zero && targetWnd == IntPtr.Zero)
            {
                targetWnd = gti.hwndActive;
                tid = GetWindowThreadProcessId(targetWnd, out _);
            }
        }

        // 2) 键盘布局
        ushort langId = 0;
        if (tid != 0)
        {
            IntPtr hkl = GetKeyboardLayout(tid);
            langId = (ushort)((ulong)hkl & 0xFFFF);
        }

        ushort primary = (ushort)(langId & 0x3FF);

        if (primary == LangJapanese) return InputState.Japanese;
        if (primary == LangKorean) return InputState.Korean;
        if (primary != LangChinese) return InputState.EnglishKeyboard;

        // 中文布局下跟踪 Shift 键切换（免 UIPI 阻断）
        UpdateShiftToggle();

        // 3) 中文布局：查询 IME 状态（同窗口 40ms 内复用上次结果，见 ImeQueryMinIntervalMs）
        if (targetWnd == IntPtr.Zero && fgWindow == IntPtr.Zero)
            return _lastValidChineseSubState ?? InputState.Chinese;

        long now = Environment.TickCount64;
        if (targetWnd == _imeCacheWnd && now - _imeCacheAt < ImeQueryMinIntervalMs)
            return _imeCacheState;

        InputState state = QueryChineseState(targetWnd, fgWindow);
        _imeCacheWnd = targetWnd;
        _imeCacheAt = now;
        _imeCacheState = state;
        return state;
    }

    /// <summary>
    /// 中文布局下的多级状态检测：
    /// 1. 优先使用 IMM32 经典查询（查询焦点控件 hwndFocus，失败再查顶层窗口）；
    /// 2. 失败时（如高权限应用 UIPI 阻断 SendMessage，或纯 TSF 应用）回退到 TSF 全局状态仓检测；
    /// 3. 仍无法查询时保持上一次有效中/英状态（或 Shift 切换状态），避免草率武断降级为英文。
    /// </summary>
    private static InputState QueryChineseState(IntPtr targetWnd, IntPtr fgWindow)
    {
        // Tier 1: IMM32 焦点窗口 / 顶层窗口 IMC 查询
        int? openStatus = targetWnd != IntPtr.Zero ? QueryIme(targetWnd, IMC_GETOPENSTATUS) : null;
        if (!openStatus.HasValue && fgWindow != IntPtr.Zero && fgWindow != targetWnd)
            openStatus = QueryIme(fgWindow, IMC_GETOPENSTATUS);

        if (openStatus.HasValue)
        {
            InputState state = openStatus.Value != 0 ? InputState.Chinese : InputState.English;
            _lastValidChineseSubState = state;
            return state;
        }

        int? conversion = targetWnd != IntPtr.Zero ? QueryIme(targetWnd, IMC_GETCONVERSIONMODE) : null;
        if (!conversion.HasValue && fgWindow != IntPtr.Zero && fgWindow != targetWnd)
            conversion = QueryIme(fgWindow, IMC_GETCONVERSIONMODE);

        if (conversion.HasValue)
        {
            InputState state = (conversion.Value & 0x1) != 0 ? InputState.Chinese : InputState.English;
            _lastValidChineseSubState = state;
            return state;
        }

        // Tier 2: TSF（Text Services Framework）全局状态仓检测（方案四：免 UIPI 窗口消息限制）
        bool? tsfState = QueryTsfGlobalState();
        if (tsfState.HasValue)
        {
            InputState state = tsfState.Value ? InputState.Chinese : InputState.English;
            _lastValidChineseSubState = state;
            return state;
        }

        // Tier 3: 保持当前有效状态或 Shift 触发状态（若从未获取过则默认中文）
        return _lastValidChineseSubState ?? InputState.Chinese;
    }

    /// <summary>向 IME 窗口发送 WM_IME_CONTROL 查询（带超时，避免在无响应窗口上卡住）</summary>
    private static int? QueryIme(IntPtr hwnd, int command)
    {
        try
        {
            IntPtr imeWnd = ImmGetDefaultIMEWnd(hwnd);
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

    /// <summary>
    /// 从 TSF 全局状态仓读取输入法状态（系统级 COM 接口，不受 UIPI 消息拦截影响）。
    /// </summary>
    private static bool? QueryTsfGlobalState()
    {
        try
        {
            if (TF_GetGlobalCompartment(out ITfCompartmentMgr compMgr) != 0 || compMgr is null)
                return null;

            // 1. 读取 KEYBOARD_OPENCLOSE 仓（开启 vs 关闭）
            Guid guidOpenClose = GuidCompartmentKeyboardOpenClose;
            if (compMgr.GetCompartment(ref guidOpenClose, out ITfCompartment compOpenClose) == 0 && compOpenClose is not null)
            {
                int? val = ReadCompartmentInt(compOpenClose);
                if (val.HasValue)
                    return val.Value != 0;
            }

            // 2. 读取 KEYBOARD_INPUTMODE_CONVERSION 仓（转换模式）
            Guid guidConversion = GuidCompartmentKeyboardConversion;
            if (compMgr.GetCompartment(ref guidConversion, out ITfCompartment compConv) == 0 && compConv is not null)
            {
                int? val = ReadCompartmentInt(compConv);
                if (val.HasValue)
                    return (val.Value & 0x1) != 0; // TF_CONVERSIONMODE_NATIVE (0x1 = 中文)
            }
        }
        catch
        {
            // 忽略 COM 互操作异常
        }
        return null;
    }

    private static int? ReadCompartmentInt(ITfCompartment comp)
    {
        IntPtr pVar = Marshal.AllocHGlobal(24);
        try
        {
            VariantInit(pVar);
            int hr = comp.GetValue(pVar);
            if (hr == 0) // S_OK
            {
                short vt = Marshal.ReadInt16(pVar);
                // VT_I4 = 3, VT_UI4 = 19, VT_INT = 22
                if (vt is 3 or 19 or 22)
                {
                    return Marshal.ReadInt32(pVar, 8);
                }
                // VT_BOOL = 11 (-1 为 true, 0 为 false)
                if (vt == 11)
                {
                    short b = Marshal.ReadInt16(pVar, 8);
                    return b != 0 ? 1 : 0;
                }
            }
        }
        catch
        {
            // 忽略读取异常
        }
        finally
        {
            VariantClear(pVar);
            Marshal.FreeHGlobal(pVar);
        }
        return null;
    }

    /// <summary>
    /// 监听物理 Shift 单击切换（免权限限制）。
    /// 当在提权窗口（如 Quicker 管理员权限）中由于 UIPI 拦截无法通过窗口消息刺探 IMC 时，
    /// 捕捉用户的 Shift 单击动作以实现自适应中英切换。
    /// </summary>
    private static void UpdateShiftToggle()
    {
        bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
        if (shiftDown)
        {
            if (!_shiftWasDown)
            {
                _shiftWasDown = true;
                _shiftDownAt = Environment.TickCount64;
            }
        }
        else if (_shiftWasDown)
        {
            _shiftWasDown = false;
            long duration = Environment.TickCount64 - _shiftDownAt;
            // 典型单独快速轻击 Shift 键（15ms ~ 500ms），判定为一次中英状态翻转
            if (duration is > 15 and < 500)
            {
                InputState current = _lastValidChineseSubState ?? InputState.Chinese;
                _lastValidChineseSubState = current == InputState.Chinese ? InputState.English : InputState.Chinese;
                _imeCacheAt = 0; // 立即失效缓存，使光标符号在下一个检测周期即时更新
            }
        }
    }
}
