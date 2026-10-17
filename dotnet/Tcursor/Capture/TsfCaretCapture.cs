// TSF 光标捕获（进程内读取）：面向微信 4.x 等“自绘 + 无障碍封闭”的应用。
//
// 原理：向目标进程注入一段极小的一次性壳代码（取数后即释放、不驻留），在其输入线程上通过 TSF
// （文本服务框架）读取当前选区（即光标）矩形，写回共享内存后由本进程读取。这是目前已知对
// 微信 4.x 唯一可靠的取光标方式（该版本无系统光标、无障碍树亦不对外暴露）。
// 壳代码来自 MIT 许可的公开参考实现（GetCaretPosEx，© Tebayaki；许可文本见 THIRD-PARTY-NOTICES.txt）。
//
// 护栏与约束：
//  · 仅 64 位目标进程（32 位目标直接放弃）；
//  · 跳过自身进程、系统关键进程与外壳/Chromium 窗口（桌面、资源管理器、Chromium 系浏览器与
//    Electron 应用的光标由既有后端覆盖，无需注入）；
//  · 每次调用 = 注入 → 读取 → 释放；目标无响应由壳内 200ms 发送超时兜底（本侧等待封顶 2s；
//    仅“远程线程未确认退出”这一极端情形保留已分配内存不释放——宁可微泄漏也不冒释放竞态，
//    其余失败路径均已即时释放）；
//  · 无光标（全零）或失败后按 pid 节流 300ms；成功不节流——避免对无光标目标高频注入；
//  · 本后端只作为捕获链的最后手段（其余后端先行；由 AppConfig.TsfFallback 控制是否追加）。
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Tcursor.Core;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Capture;

/// <summary>TSF（文本服务框架）进程内光标读取；最后手段后端，说明见文件头。</summary>
internal sealed class TsfCaretCapture : ICaretCapture
{
    /// <summary>x64 壳代码（Base64，来自参考实现构建产物；许可见 THIRD-PARTY-NOTICES.txt）。</summary>
    private const string Shellcode64 =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABrnppSh2UjT6uenH1oPjxQAeiAqiEg0hGT4ABgsGe4blNldFdpbmRvd3NIb29rRXhXAAAAVW5ob29rV2luZG93c0hvb2tFeABDYWxsTmV4dEhvb2tFeAAAAAAAAFNlbmRNZXNzYWdlVGltZW91dFcAQ29DcmVhdGVJbnN0YW5jZQAAAAAAAAAASIlcJAhIiXQkEFdIg+wgSYvYSIvyi/mFyXgjSIXbdB6LBQb///9BOUAQdRJIjQ3d/v//6JgBAACJBfL+//9Iiw3L/v//SI0VdP///+jnAgAASIXAdRBIi1wkMEiLdCQ4SIPEIF/DTIvLTIvGi9czyUiLXCQwSIt0JDhIg8QgX0j/4MzMzMzMzDPAw8zMzMzMQFNWSIPsSIvySIvZSIXJdQy4VwAHgEiDxEheW8NIi0kISI1UJGBIiVQkKEG4/////0iNVCQwSIl8JEAz/0iJVCQgiXwkYIvWSIsBRI1PAf9QKIXAeHJIOXwkMHRrOXwkYHRlSItLCEiNVCR4SIl8JHhIiwH/UEiL+IXAeDJIi0wkeEiFyXQoSIsBSI1UJHBMi0QkMEyNSxBIiVQkIIvW/1AgSItMJHiL+EiLAf9QEEiLTCQwSIsB/1AQi8dIi3wkQEiDxEheW8NIi3wkQLgBAAAASIPESF5bw8zMzMzMzMxIhcl0VEiF0nRPTYXAdEpIiwJIhcB1HUi4wAAAAAAAAEZIOUIIdCxJxwAAAAAAuAJAAIDDSbkD6ICqISDSEUk7wXXkSLiT4ABgsGe4bkg5Qgh11EmJCDPAw7hXAAeAw8xAU0iD7EBIi9lIjZHYAAAASItJCOhPAQAASIXAdQu4AQAAAEiDxEBbwzPJx0QkWAEAAABIjVQkaEiJTCRoSIlUJCBMjUt4M9JIiUwkYEiJTCQwiUwkUEiNS2hEjUIX/9CFwA+I7wAAAEiLTCRoSIXJD4ThAAAASIsBSI1UJFD/UBiFwA+IhQAAAEiLTCRoSI1UJGBIiwH/UDiFwHhxSItMJGBIhcl0bEiLAUiNVCQw/1AwhcB4WEiLTCQwSIXJdGZIjUNISIlLMEiJQyhMjUMoSI0Vyf7//0G5AwAAAEiJEEiNBdH9//9IiUNQSI1UJFhIiUNYSI0Fxf3//0iJQ2BIiwFIiVQkIItUJFD/UBhIi0wkYEiLVCQwSIXSdA5IiwJIi8r/UBBIi0wkYEiFyXQGSIsB/1AQSItMJGhIhcl0BkiLAf9QEItEJFj32BvAg+AESIPEQFvDuAQAAABIg8RAW8PMzMzMzMxIiVwkCEiJbCQQSIl0JBhIiXwkIEyL2kyL0UiFyXRwSIXSdGtIY0E8g7wIjAAAAAB0XYuMCIgAAACFyXRSRYtMCiBJjQQKi3AkTQPKi2gcSQPyi3gYSQPqD7YaRTPA/89BixFJA9I6GnUZD7bLSYvDSSvThMl0Lw+2SAFI/8A6DAJ08EH/wEmDwQREO8d20TPASItcJAhIi2wkEEiLdCQYSIt8JCDDSWPAD7cMRotEjQBJA8Lr28zMSIlcJAhIiWwkEEiJdCQYSIl8JCBBVkiD7EBIixlIjZGIAAAASIv5SIvL6Bn///9IjZfEAAAASIvLSIvw6Af///9IjZecAAAASIvLSIvo6PX+//9Mi/BIhfZ0ZUiF7XRgSIXAdFtEi08YSI0VoPv//0UzwEGNSAT/1kiL8EiFwHUFjUYC6z+LVxwzwEiLTxBFM8lIiUQkMEUzwMdEJCjIAAAAiUQkIP/VSIvOSIvYQf/WSIXbdQWNQwPrCotHIOsFuAEAAABIi1wkUEiLbCRYSIt0JGBIi3wkaEiDxEBBXsM=";

    // 壳内数据布局与入口点（与参考实现一致，勿改）
    private const int OffUser32 = 0;
    private const int OffCombase = 8;
    private const int OffHwnd = 16;
    private const int OffTid = 24;
    private const int OffMsg = 28;
    private const int OffRect = 56;
    private const int ThreadProcRva = 0x4E0;

    private const uint ProcessAccess = 0x043A;     // CREATE_THREAD | QUERY_INFORMATION | VM_OPERATION | VM_WRITE | VM_READ
    private const uint MemCommitReserve = 0x3000;  // MEM_COMMIT | MEM_RESERVE
    private const uint MemRelease = 0x8000;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint ListModulesAll = 0x03;
    private const uint WaitObject0 = 0;
    private const long RetryThrottleMs = 300;
    private const int CacheMax = 256;

    private static readonly uint CaretPosMessage = RegisterWindowMessageW("WM_GET_CARET_POS");

    private static readonly HashSet<string> SkipProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "winlogon", "lsass", "services", "smss", "System", "Idle", "Registry", "MemCompression",
    };

    private static readonly HashSet<string> SkipClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "CabinetWClass", "ExploreWClass",
    };

    /// <summary>类名前缀跳过：Chromium 系（含 Electron）光标由 UIA 覆盖，无需注入</summary>
    private static readonly string[] SkipClassPrefixes = { "Chrome_WidgetWin" };

    // 仅检测工作线程读写（与 CaptureChain 同约定），无并发保护
    private readonly Dictionary<uint, long> _retryAfter = new();
    private readonly HashSet<uint> _checkedPid = new();
    private readonly HashSet<uint> _deniedPid = new();

    public bool TryGetCaret(IntPtr hwnd, out CaretRect caret)
    {
        caret = default;
        if (hwnd == IntPtr.Zero)
            return false;
        uint tid = GetWindowThreadProcessId(hwnd, out uint pid);
        if (tid == 0 || pid == 0 || pid == (uint)Environment.ProcessId)
            return false;
        if (IsSkippedClass(hwnd))
            return false;

        long now = Environment.TickCount64;
        if (_retryAfter.TryGetValue(pid, out long retryAt) && now < retryAt)
            return false;

        bool ok = false;
        if (!IsSkippedProcess(pid))
        {
            try
            {
                ok = TryRead(pid, tid, hwnd, out caret);
            }
            catch
            {
                ok = false;   // 后端约定：失败返回 false，不抛异常
            }
        }

        if (ok)
        {
            _retryAfter.Remove(pid);
        }
        else
        {
            caret = default;
            if (_retryAfter.Count >= CacheMax)
                _retryAfter.Clear();
            _retryAfter[pid] = now + RetryThrottleMs;
        }
        return ok;
    }

    private static bool IsSkippedClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(32);
        if (GetClassName(hwnd, sb, sb.Capacity) <= 0)
            return false;
        string name = sb.ToString();
        if (SkipClasses.Contains(name))
            return true;
        foreach (string prefix in SkipClassPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private bool IsSkippedProcess(uint pid)
    {
        if (_deniedPid.Contains(pid))
            return true;
        if (_checkedPid.Contains(pid))
            return false;
        string name;
        try
        {
            name = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return false;   // 读不到进程名：不拦截
        }
        if (_checkedPid.Count >= CacheMax)
            _checkedPid.Clear();
        _checkedPid.Add(pid);
        if (SkipProcesses.Contains(name))
        {
            if (_deniedPid.Count >= CacheMax)
                _deniedPid.Clear();
            _deniedPid.Add(pid);
            return true;
        }
        return false;
    }

    private static bool TryRead(uint pid, uint tid, IntPtr hwnd, out CaretRect caret)
    {
        caret = default;
        IntPtr hProcess = OpenProcess(ProcessAccess, false, pid);
        if (hProcess == IntPtr.Zero)
            return false;
        try
        {
            if (!IsWow64Process(hProcess, out bool wow64) || wow64)
                return false;   // 32 位目标不支持
            if (!TryGetModuleBases(hProcess, out IntPtr user32Base, out IntPtr combaseBase))
                return false;

            byte[] code = Convert.FromBase64String(Shellcode64);
            BitConverter.TryWriteBytes(code.AsSpan(OffUser32), (long)user32Base);
            BitConverter.TryWriteBytes(code.AsSpan(OffCombase), (long)combaseBase);
            BitConverter.TryWriteBytes(code.AsSpan(OffHwnd), (long)hwnd);
            BitConverter.TryWriteBytes(code.AsSpan(OffTid), tid);
            BitConverter.TryWriteBytes(code.AsSpan(OffMsg), CaretPosMessage);

            IntPtr mem = VirtualAllocEx(hProcess, IntPtr.Zero, (nuint)code.Length, MemCommitReserve, PageExecuteReadWrite);
            if (mem == IntPtr.Zero)
                return false;

            bool threadStarted = false;
            bool threadExited = false;
            try
            {
                if (!WriteProcessMemory(hProcess, mem, code, (nuint)code.Length, out nuint written) || (long)written != code.Length)
                    return false;   // 未启动线程：目标内存由 finally 立即释放
                FlushInstructionCache(hProcess, mem, (nuint)code.Length);

                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, mem + ThreadProcRva, mem, 0, out uint _);
                if (hThread == IntPtr.Zero)
                    return false;   // 未启动线程：目标内存由 finally 立即释放
                threadStarted = true;
                try
                {
                    threadExited = WaitForSingleObject(hThread, 2000) == WaitObject0;
                    if (!threadExited)
                        return false;   // 线程未退出：内存保留不释放（见文件头）
                    if (!GetExitCodeThread(hThread, out uint exitCode) || exitCode != 0)
                        return false;   // 壳内错误码 ≠ 0：失败（含无 TSF 上下文等）
                }
                finally
                {
                    CloseHandle(hThread);
                }

                byte[] rect = new byte[16];
                if (!ReadProcessMemory(hProcess, mem + OffRect, rect, (nuint)rect.Length, out nuint read) || (long)read != rect.Length)
                    return false;
                int left = BitConverter.ToInt32(rect, 0);
                int top = BitConverter.ToInt32(rect, 4);
                int right = BitConverter.ToInt32(rect, 8);
                int bottom = BitConverter.ToInt32(rect, 12);
                if ((left | top) == 0)
                    return false;   // 全零 = 此处无光标（与参考实现同判据）
                if (right <= left)
                    right = left + 1;
                if (bottom <= top)
                    bottom = top + 1;
                // 输拼音组字阶段与选区处理：
                // 当存在字符跨度时（right > left + 1），输入插入点恒在拼音文本末尾（right），
                // 彻底杜绝 TSF 读取首字母与 Win32 GUI 读取尾字母交替导致的来回跳跃
                int caretX = right > left + 1 ? right : left;
                caret = new CaretRect(caretX, top, caretX + 1, bottom);
                return true;
            }
            finally
            {
                // 仅“线程未启动”或“已确认退出”时释放；未退出（极端超时）保留不释放，避免释放竞态
                if (!threadStarted || threadExited)
                    VirtualFreeEx(hProcess, mem, 0, MemRelease);
            }
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private static bool TryGetModuleBases(IntPtr hProcess, out IntPtr user32Base, out IntPtr combaseBase)
    {
        user32Base = IntPtr.Zero;
        combaseBase = IntPtr.Zero;
        IntPtr[] modules = new IntPtr[512];
        if (!K32EnumProcessModulesEx(hProcess, modules, (nuint)(modules.Length * IntPtr.Size), out nuint needed, ListModulesAll))
            return false;
        int count = Math.Min(modules.Length, (int)(needed / (uint)IntPtr.Size));
        var name = new StringBuilder(64);
        for (int i = 0; i < count; i++)
        {
            name.Clear();
            if (K32GetModuleBaseNameW(hProcess, modules[i], name, name.Capacity) == 0)
                continue;
            string moduleName = name.ToString();
            bool wantUser32 = user32Base == IntPtr.Zero && moduleName.Equals("user32.dll", StringComparison.OrdinalIgnoreCase);
            bool wantCombase = combaseBase == IntPtr.Zero && moduleName.Equals("combase.dll", StringComparison.OrdinalIgnoreCase);
            if (!wantUser32 && !wantCombase)
                continue;
            if (!K32GetModuleInformation(hProcess, modules[i], out MODULEINFO info, (uint)Marshal.SizeOf<MODULEINFO>()))
                return false;
            if (wantUser32)
                user32Base = info.lpBaseOfDll;
            else
                combaseBase = info.lpBaseOfDll;
            if (user32Base != IntPtr.Zero && combaseBase != IntPtr.Zero)
                break;
        }
        return user32Base != IntPtr.Zero && combaseBase != IntPtr.Zero;
    }
}
