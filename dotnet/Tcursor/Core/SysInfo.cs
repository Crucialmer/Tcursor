using System.Runtime.InteropServices;
using static Tcursor.Win32.NativeMethods;

namespace Tcursor.Core;

/// <summary>系统信息辅助（空闲时间等）</summary>
internal static class SysInfo
{
    private static readonly int LiiSize = Marshal.SizeOf<LASTINPUTINFO>();   // 常量：避免每 tick 运行时求尺寸

    /// <summary>自最后一次键鼠输入以来的毫秒数</summary>
    public static uint GetIdleMs()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)LiiSize };
        if (!GetLastInputInfo(ref lii))
            return 0;
        uint now = (uint)Environment.TickCount64;
        uint diff = unchecked(now - lii.dwTime);
        // 若因 QPC / User32 时钟微小抖动出现 diff 超过 int.MaxValue（相当于负差值），视为 0ms 空闲（刚输入）
        if ((int)diff < 0)
            return 0;
        return diff;
    }
}
