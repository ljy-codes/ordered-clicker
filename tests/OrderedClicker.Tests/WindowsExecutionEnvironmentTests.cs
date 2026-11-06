#if WINDOWS
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class WindowsExecutionEnvironmentTests
{
    public static void Run()
    {
        var binding = new TargetWindowBinding
        {
            Platform = "Windows", ApplicationId = @"C:\app\target.exe",
            WindowTitle = "任务"
        };
        var foreground = new WindowsTargetWindowSnapshot(
            new IntPtr(101), binding.ApplicationId, binding.WindowTitle, true);
        TestAssert.True(WindowsExecutionEnvironment.EvaluateTarget(
            binding, foreground, foreground) is null, "绑定窗口中的点位应通过");
        TestAssert.True(WindowsExecutionEnvironment.EvaluateTarget(
            binding, foreground, foreground,
            foreground with { Handle = new IntPtr(102) }) is not null,
            "计划点在目标窗口而实际鼠标因舍入落入邻窗时必须暂停");
        TestAssert.True(WindowsExecutionEnvironment.EvaluateTarget(
            binding, foreground, foreground, foreground) is null,
            "实际鼠标也命中绑定窗口时应通过");
        TestAssert.True(WindowsExecutionEnvironment.EvaluateTarget(
            binding, foreground, foreground with { Handle = new IntPtr(102) }) is not null,
            "同进程的另一个窗口遮挡目标时也必须暂停");
        TestAssert.True(WindowsExecutionEnvironment.EvaluateTarget(
            binding, foreground with { Available = false }, foreground) is not null,
            "最小化或不可见窗口不能执行");
        TestAssert.True(WindowsExecutionEnvironment.EvaluateTarget(
            binding, foreground with { WindowTitle = "另一个任务" }, foreground) is not null,
            "同应用不同窗口不能通过绑定");
        TestAssert.True(WindowsExecutionEnvironment.EvaluateTarget(
            binding, foreground with { ApplicationId = @"C:\other\app.exe" }, foreground) is not null,
            "其他进程的同名窗口不能通过绑定");
        binding.Platform = "macOS";
        TestAssert.True(WindowsExecutionEnvironment.EvaluateTarget(
            binding, foreground, foreground) is not null, "跨平台绑定必须重新确认");
    }
}
#endif
