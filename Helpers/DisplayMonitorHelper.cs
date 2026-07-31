using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace SoundBoard.Helpers;

public class DisplayMonitorInfo
{
    public int Index { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public Rect Bounds { get; set; }
    public Rect WorkingArea { get; set; }
    public bool IsPrimary { get; set; }
}

public static class DisplayMonitorHelper
{
    #region Win32 P/Invoke
    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private const uint MONITORINFOF_PRIMARY = 0x00000001;
    #endregion

    public static List<DisplayMonitorInfo> GetDisplayMonitors()
    {
        var monitors = new List<DisplayMonitorInfo>();
        int index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(mi);

            if (GetMonitorInfo(hMonitor, ref mi))
            {
                var bounds = new Rect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                var workArea = new Rect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
                bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;

                monitors.Add(new DisplayMonitorInfo
                {
                    Index = index++,
                    DeviceName = mi.szDevice ?? $"Monitor {index}",
                    Bounds = bounds,
                    WorkingArea = workArea,
                    IsPrimary = isPrimary
                });
            }

            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            // Fallback to system primary bounds
            monitors.Add(new DisplayMonitorInfo
            {
                Index = 0,
                DeviceName = "Primary Display",
                Bounds = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
                WorkingArea = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
                IsPrimary = true
            });
        }

        return monitors;
    }
}
