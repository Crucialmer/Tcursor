using System.Runtime.InteropServices;
using System.Text;

namespace Tcursor.Win32;

/// <summary>集中管理所有 Win32 P/Invoke（仅包含本程序需要的子集）</summary>
internal static class NativeMethods
{
    // ---------------- user32 ----------------
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] internal static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    /// <summary>GetKeyState 低位 = 切换键（CapsLock 等）的“开/关”状态（持续有效；勿用 GetAsyncKeyState 低位代替——那是“自上次读取后是否按过”的一次性脉冲）</summary>
    [DllImport("user32.dll")] internal static extern short GetKeyState(int vKey);
    [DllImport("user32.dll")] internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    // ---------------- imm32 ----------------
    [DllImport("imm32.dll")] internal static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    // ---------------- shcore ----------------
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // ---------------- gdi32 ----------------
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr hObject);

    // ---------------- user32（图标） ----------------
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr hIcon);

    // ---------------- oleacc ----------------
    [DllImport("oleacc.dll")] internal static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwId, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);

    internal delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    // ---------------- 常量 ----------------
    internal const uint
        WM_IME_CONTROL = 0x0283,
        SMTO_ABORTIFHUNG = 0x0002,
        EVENT_SYSTEM_FOREGROUND = 0x0003,
        WINEVENT_OUTOFCONTEXT = 0x0000,
        WINEVENT_SKIPOWNPROCESS = 0x0002,
        ULW_ALPHA = 0x00000002,
        MONITOR_DEFAULTTONEAREST = 2;

    internal const int
        SW_HIDE = 0,
        SW_SHOWNOACTIVATE = 4,
        VK_CAPITAL = 0x14,
        AC_SRC_OVER = 0,
        AC_SRC_ALPHA = 1;
}
