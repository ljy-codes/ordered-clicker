using System.ComponentModel;
using System.Runtime.InteropServices;
using OrderedClicker.Models;
using OrderedClicker.Native;

namespace OrderedClicker.Services;

public sealed class MonitorService
{
    public CapturedPoint CaptureCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "获取鼠标坐标失败。");
        }

        var monitorHandle = NativeMethods.MonitorFromPoint(
            cursor,
            NativeMethods.MonitorDefaultToNearest);
        var monitor = ReadMonitor(monitorHandle);
        return new CapturedPoint(
            cursor.X,
            cursor.Y,
            monitor.DeviceName,
            monitor.Bounds,
            monitor.Dpi);
    }

    public ScreenBounds GetVirtualScreenBounds()
    {
        var bounds = SystemInformation.VirtualScreen;
        return new ScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    public MonitorSnapshot? FindMonitor(string deviceName)
    {
        var screen = Screen.AllScreens.FirstOrDefault(candidate =>
            string.Equals(candidate.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        if (screen is null)
        {
            return null;
        }

        var center = new NativeMethods.Point
        {
            X = screen.Bounds.Left + screen.Bounds.Width / 2,
            Y = screen.Bounds.Top + screen.Bounds.Height / 2
        };
        var monitorHandle = NativeMethods.MonitorFromPoint(
            center,
            NativeMethods.MonitorDefaultToNearest);
        return ReadMonitor(monitorHandle);
    }

    private static MonitorSnapshot ReadMonitor(IntPtr monitorHandle)
    {
        var info = new NativeMethods.MonitorInfoEx
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
            DeviceName = string.Empty
        };

        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "获取显示器信息失败。");
        }

        var dpi = 96u;
        try
        {
            if (NativeMethods.GetDpiForMonitor(
                    monitorHandle,
                    NativeMethods.MonitorDpiType.Effective,
                    out var dpiX,
                    out _) == 0)
            {
                dpi = dpiX;
            }
        }
        catch (DllNotFoundException)
        {
            dpi = 96;
        }

        return new MonitorSnapshot(
            info.DeviceName,
            new ScreenBounds(
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left,
                info.Monitor.Bottom - info.Monitor.Top),
            dpi);
    }
}
