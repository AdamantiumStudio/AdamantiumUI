using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>The monitors as Win32 tells them, read live: they are plugged in and unplugged while an application runs.
/// </summary>
internal sealed class WindowsDisplays : IDisplayPlatform
{
    private const int InsufficientBuffer = 122;

    /// <summary>Every monitor as one rectangle; it starts at a negative origin when a monitor sits left of the primary.
    /// </summary>
    public Rect VirtualScreen => new(
        Win32Interop.GetSystemMetrics(SystemMetrics.Xvirtualscreen),
        Win32Interop.GetSystemMetrics(SystemMetrics.Yvirtualscreen),
        Win32Interop.GetSystemMetrics(SystemMetrics.CxVirtualscreen),
        Win32Interop.GetSystemMetrics(SystemMetrics.CyVirtualscreen));

    /// <summary>The monitors; the device name is what stays with a monitor between runs.</summary>
    public IReadOnlyList<ScreenInfo> Screens
    {
        get
        {
            var screens = new List<ScreenInfo>();
            Win32Interop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr deviceContext, ref RECT bounds, IntPtr data) =>
            {
                var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
                if (Win32Interop.GetMonitorInfo(monitor, ref info))
                {
                    var scale = Win32Interop.GetDpiForMonitor(monitor, Win32Interop.MdtEffectiveDpi, out var dpi, out _) == 0 && dpi > 0
                        ? dpi / 96.0
                        : 1.0;
                    screens.Add(new ScreenInfo(info.DeviceName, ToRect(info.Monitor), ToRect(info.WorkArea), scale,
                        (info.Flags & MONITORINFOEX.PrimaryFlag) != 0, RefreshRateOf(monitor)));
                }

                return true;
            }, IntPtr.Zero);
            return screens;
        }
    }

    /// <summary>The exact refresh rate of <paramref name="monitor"/> in hertz; 0 when Windows does not say.</summary>
    internal static double RefreshRateOf(IntPtr monitor)
    {
        var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
        if (!Win32Interop.GetMonitorInfo(monitor, ref info))
        {
            return 0;
        }

        DISPLAYCONFIG_PATH_INFO[] paths;
        uint pathCount;
        int result;
        do
        {
            if (Win32Interop.GetDisplayConfigBufferSizes(Win32Interop.QdcOnlyActivePaths, out pathCount, out var modeCount) != 0)
            {
                return 0;
            }

            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            result = Win32Interop.QueryDisplayConfig(Win32Interop.QdcOnlyActivePaths, ref pathCount, paths, ref modeCount,
                modes, IntPtr.Zero);
        }
        while (result == InsufficientBuffer);

        if (result != 0)
        {
            return 0;
        }

        for (var i = 0; i < pathCount; i++)
        {
            var path = paths[i];
            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                Type = DISPLAYCONFIG_SOURCE_DEVICE_NAME.GetSourceName,
                Size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                AdapterIdLow = path.SourceAdapterIdLow,
                AdapterIdHigh = path.SourceAdapterIdHigh,
                Id = path.SourceId
            };
            if (Win32Interop.DisplayConfigGetDeviceInfo(ref source) == 0 && source.GdiDeviceName == info.DeviceName
                && path.RefreshRateDenominator != 0)
            {
                return (double)path.RefreshRateNumerator / path.RefreshRateDenominator;
            }
        }

        return 0;
    }

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
