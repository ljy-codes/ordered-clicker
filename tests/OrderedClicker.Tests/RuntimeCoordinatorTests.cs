using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Tests;

internal static class RuntimeCoordinatorTests
{
    public static async Task RunAsync()
    {
        LatestProgressKeepsOnlyNewestValue();
        await ReplaceableDelayRunsOnlyLatestActionAsync();
        await SafetyWatchdogTriggersOutsideUiThreadAsync();
    }

    private static void LatestProgressKeepsOnlyNewestValue()
    {
        var buffer = new LatestExecutionProgress();
        var first = new ExecutionProgress(1, 1, 1, 1, 1, 2);
        var second = new ExecutionProgress(1, 1, 1, 1, 2, 2);

        buffer.Report(first);
        buffer.Report(second);

        TestAssert.True(buffer.TryConsume(out var actual), "应能读取最新进度");
        TestAssert.Equal(second, actual, "旧进度应被覆盖");
        TestAssert.True(!buffer.TryConsume(out _), "同一进度只能消费一次");

        buffer.Report(first);
        buffer.Clear();
        TestAssert.True(!buffer.TryConsume(out _), "清空后不应残留旧进度");
    }

    private static async Task ReplaceableDelayRunsOnlyLatestActionAsync()
    {
        using var delay = new ReplaceableDelay();
        var value = 0;
        var first = delay.RunAsync(
            TimeSpan.FromMilliseconds(80),
            _ =>
            {
                value = 1;
                return Task.CompletedTask;
            });
        await Task.Delay(10);
        var second = delay.RunAsync(
            TimeSpan.FromMilliseconds(20),
            _ =>
            {
                value = 2;
                return Task.CompletedTask;
            });

        await Task.WhenAll(first, second);

        TestAssert.Equal(2, value, "替换延时后只能执行最后一个动作");
        delay.Cancel();
    }

    private static async Task SafetyWatchdogTriggersOutsideUiThreadAsync()
    {
        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var watchdog = new SafetyCornerWatchdog(
            () => true,
            () => new Point(1, 1),
            () => new ScreenBounds(0, 0, 100, 100),
            () => SafetyCorner.TopLeft,
            () => 10,
            () => TimeSpan.FromMilliseconds(15),
            () => stopped.TrySetResult(),
            TimeSpan.FromMilliseconds(5));

        watchdog.Start();

        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
