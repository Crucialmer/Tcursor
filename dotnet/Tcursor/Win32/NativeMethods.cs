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
    /// <summary>读取指定虚拟键的实时物理按下状态（高位为 1 表示当前处于按下状态，免 UIPI 隔离）</summary>
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool GetCursorInfo(out CURSORINFO pci);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] internal static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    /// <summary>窗口拖动（客户区自绘标题区）用到的消息与命中码</summary>
    internal const int WM_NCLBUTTONDOWN = 0x00A1;
    internal const int HTCAPTION = 2;

    // ---------------- dwmapi ----------------
    /// <summary>DWM 窗口属性（Win11 圆角、云母/亚克力背景、深浅模式等；低版本调用失败即忽略）</summary>
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    internal const int DWMWA_WINDOW_CORNER_RADIUS = 33;
    internal const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    internal const int DWMWCP_ROUND = 2;

    internal const int DWMSBT_AUTO = 0;
    internal const int DWMSBT_NONE = 1;
    internal const int DWMSBT_MAINWINDOW = 2;        // Mica
    internal const int DWMSBT_TRANSIENTWINDOW = 3;   // Acrylic
    internal const int DWMSBT_TABBEDWINDOW = 4;      // Mica Alt

    // ---------------- imm32 ----------------
    [DllImport("imm32.dll")] internal static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    // ---------------- msctf (TSF 全局状态仓，免疫 UIPI 拦截) ----------------
    [DllImport("msctf.dll", PreserveSig = true)]
    internal static extern int TF_GetGlobalCompartment(out ITfCompartmentMgr ppCompMgr);

    [ComImport, Guid("7dcf57ac-18ad-438b-824d-979bffb74b7c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfCompartmentMgr
    {
        [PreserveSig] int GetCompartment(ref Guid rguid, out ITfCompartment comp);
        [PreserveSig] int ClearCompartment(uint clientId, ref Guid rguid);
        [PreserveSig] int EnumCompartments(out IntPtr enumComp);
    }

    [ComImport, Guid("bb08f7a9-607a-4384-8623-056892b64371"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfCompartment
    {
        [PreserveSig] int SetValue(uint clientId, IntPtr pvarValue);
        [PreserveSig] int GetValue(IntPtr pvarValue);
    }

    // ---------------- oleaut32 ----------------
    [DllImport("oleaut32.dll")] internal static extern void VariantInit(IntPtr pvarg);
    [DllImport("oleaut32.dll")] internal static extern int VariantClear(IntPtr pvarg);

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

    // ---------------- kernel32（TSF 进程内读取：进程/内存/线程） ----------------
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint dwFreeType);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesWritten);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesRead);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, nuint dwSize);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, nuint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool K32EnumProcessModulesEx(IntPtr hProcess, [Out] IntPtr[] lphModule, nuint cb, out nuint lpcbNeeded, uint dwFilterFlag);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint K32GetModuleBaseNameW(IntPtr hProcess, IntPtr hModule, StringBuilder lpBaseName, int nSize);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool K32GetModuleInformation(IntPtr hProcess, IntPtr hModule, out MODULEINFO lpmodinfo, uint cb);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MODULEINFO
    {
        public IntPtr lpBaseOfDll;
        public uint SizeOfImage;
        public IntPtr EntryPoint;
    }

    // ---------------- user32（自注册窗口消息：TSF 取光标通知用） ----------------
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessageW(string lpString);

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

    [StructLayout(LayoutKind.Sequential)]
    internal struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

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
        MONITOR_DEFAULTTONEAREST = 2,
        CURSOR_SHOWING = 0x00000001;

    internal const int
        SW_HIDE = 0,
        SW_SHOWNOACTIVATE = 4,
        VK_SHIFT = 0x10,
        VK_CAPITAL = 0x14,
        IDC_IBEAM = 32513,
        AC_SRC_OVER = 0,
        AC_SRC_ALPHA = 1;
}
