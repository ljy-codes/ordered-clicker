using System.Diagnostics;
using System.Text;
using OrderedClicker.Models;
using OrderedClicker.Native;

namespace OrderedClicker.Services;

public sealed record WindowsTargetWindowSnapshot(
    IntPtr Handle,
    string ApplicationId,
    string WindowTitle,
    bool Available);

public static class WindowsExecutionEnvironment
{
    public static string CaptureDisplaySignature(MonitorService monitors)
    {
        return string.Join("|", Screen.AllScreens
            .OrderBy(screen => screen.DeviceName, StringComparer.Ordinal)
            .Select(screen =>
            {
                var monitor = monitors.FindMonitor(screen.DeviceName);
                return $"{screen.DeviceName}:{monitor?.Bounds}:{monitor?.Dpi}";
            }));
    }

    public static bool IsInputDesktopAvailable()
    {
        var desktop = NativeMethods.OpenInputDesktop(0, false, 0x0001);
        if (desktop == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.CloseDesktop(desktop);
        return true;
    }

    public static TargetWindowBinding CaptureTarget()
    {
        var snapshot = ReadWindow(NativeMethods.GetForegroundWindow());
        if (!snapshot.Available
            || string.IsNullOrWhiteSpace(snapshot.WindowTitle)
            || string.IsNullOrWhiteSpace(snapshot.ApplicationId))
        {
            throw new InvalidOperationException("无法读取目标窗口，请切换到可见的目标程序后重新绑定。");
        }

        NativeMethods.GetWindowThreadProcessId(snapshot.Handle, out var processId);
        if (processId == Environment.ProcessId)
        {
            throw new InvalidOperationException("请切换到需要操作的程序后绑定，不能绑定连点器自身。");
        }

        return new TargetWindowBinding
        {
            Platform = "Windows",
            ApplicationId = snapshot.ApplicationId,
            WindowTitle = snapshot.WindowTitle
        };
    }

    public static string? CheckTarget(TargetWindowBinding? binding, ExecutionPlanPoint point)
    {
        var foregroundHandle = NativeMethods.GetForegroundWindow();
        var hitHandle = NativeMethods.WindowFromPoint(
            new NativeMethods.Point { X = point.X, Y = point.Y });
        if (!NativeMethods.GetCursorPos(out var actual))
        {
            return "无法确认鼠标位置，请检查当前桌面";
        }
        // MoveTo accepts small rounding errors; those pixels must hit the same window.
        var actualHitHandle = Math.Abs((long)actual.X - point.X) <= 2
            && Math.Abs((long)actual.Y - point.Y) <= 2
            ? NativeMethods.WindowFromPoint(actual)
            : hitHandle;
        NativeMethods.GetWindowThreadProcessId(foregroundHandle, out var foregroundProcess);
        NativeMethods.GetWindowThreadProcessId(hitHandle, out var hitProcess);
        NativeMethods.GetWindowThreadProcessId(actualHitHandle, out var actualHitProcess);
        if (foregroundProcess == Environment.ProcessId || hitProcess == Environment.ProcessId
            || actualHitProcess == Environment.ProcessId)
        {
            return "请切换到目标程序，并移开遮挡点位的连点器窗口";
        }
        if (binding is null)
        {
            return null;
        }

        var foreground = ReadWindow(foregroundHandle);
        var hit = ReadWindow(hitHandle);
        return EvaluateTarget(binding, foreground, hit, ReadWindow(actualHitHandle));
    }

    public static string? EvaluateTarget(
        TargetWindowBinding binding,
        WindowsTargetWindowSnapshot foreground,
        WindowsTargetWindowSnapshot hit,
        WindowsTargetWindowSnapshot? actualHit = null)
    {
        if (!string.Equals(binding.Platform, "Windows", StringComparison.OrdinalIgnoreCase))
        {
            return "目标窗口来自其他平台，请重新绑定或解除绑定";
        }

        if (!foreground.Available
            || !string.Equals(binding.ApplicationId, foreground.ApplicationId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(binding.WindowTitle, foreground.WindowTitle,
                StringComparison.Ordinal))
        {
            return "目标窗口未在前台，或标题已变化，请切换到绑定窗口";
        }

        if (!hit.Available || hit.Handle != foreground.Handle
            || (actualHit is not null
                && (!actualHit.Available || actualHit.Handle != foreground.Handle)))
        {
            return "目标点位被其他窗口遮挡，或已移出绑定窗口";
        }

        return null;
    }

    private static WindowsTargetWindowSnapshot ReadWindow(IntPtr handle)
    {
        var root = handle == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.GetAncestor(handle, 2);
        if (root == IntPtr.Zero
            || NativeMethods.IsIconic(root)
            || !NativeMethods.IsWindowVisible(root))
        {
            return new(root, string.Empty, string.Empty, false);
        }

        try
        {
            NativeMethods.GetWindowThreadProcessId(root, out var processId);
            using var process = Process.GetProcessById(checked((int)processId));
            var application = process.MainModule?.FileName ?? string.Empty;
            var title = new StringBuilder(1024);
            NativeMethods.GetWindowText(root, title, title.Capacity);
            return new(root, application, title.ToString(), !string.IsNullOrEmpty(application));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or ArgumentException)
        {
            return new(root, string.Empty, string.Empty, false);
        }
    }
}
