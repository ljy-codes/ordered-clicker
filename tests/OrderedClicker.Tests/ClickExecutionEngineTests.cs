using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class ClickExecutionEngineTests
{
    public static async Task RunAsync()
    {
        await ExecutesPointsInOrderForEveryLoop();
        await SkipsDisabledPoints();
        await WaitsWhilePaused();
        await CancelsDuringDelay();
    }

    private static async Task ExecutesPointsInOrderForEveryLoop()
    {
        var mouse = new RecordingMouseController();
        var engine = new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask);
        var profile = new ClickProfile
        {
            TotalLoops = 2,
            LoopDelayMs = 0,
            Points =
            [
                CreatePoint(10, 20, 2),
                CreatePoint(30, 40, 1)
            ]
        };

        await engine.ExecuteAsync(profile, new AsyncPauseGate(), null, CancellationToken.None);

        TestAssert.Equal("M:10,20|C|C|M:30,40|C|M:10,20|C|C|M:30,40|C",
            string.Join("|", mouse.Events), "执行顺序、单点次数和总循环次数应一致");
    }

    private static async Task SkipsDisabledPoints()
    {
        var mouse = new RecordingMouseController();
        var engine = new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask);
        var disabled = CreatePoint(10, 20, 3);
        disabled.Enabled = false;
        var profile = new ClickProfile
        {
            TotalLoops = 1,
            Points = [disabled, CreatePoint(30, 40, 1)]
        };

        await engine.ExecuteAsync(profile, new AsyncPauseGate(), null, CancellationToken.None);

        TestAssert.Equal("M:30,40|C", string.Join("|", mouse.Events), "禁用点位不应执行");
    }

    private static async Task WaitsWhilePaused()
    {
        var mouse = new RecordingMouseController();
        var engine = new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask);
        var pauseGate = new AsyncPauseGate();
        pauseGate.Pause();
        var profile = new ClickProfile
        {
            TotalLoops = 1,
            Points = [CreatePoint(10, 20, 1)]
        };

        var execution = engine.ExecuteAsync(profile, pauseGate, null, CancellationToken.None);
        await Task.Delay(50);
        TestAssert.Equal(0, mouse.Events.Count, "暂停时不应移动或点击鼠标");

        pauseGate.Resume();
        await execution;

        TestAssert.Equal("M:10,20|C", string.Join("|", mouse.Events), "恢复后应继续执行");
    }

    private static async Task CancelsDuringDelay()
    {
        var mouse = new RecordingMouseController();
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new ClickExecutionEngine(mouse, async (_, cancellationToken) =>
        {
            delayStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        var profile = new ClickProfile
        {
            TotalLoops = 1,
            Points = [CreatePoint(10, 20, 2)]
        };
        using var cancellation = new CancellationTokenSource();

        var execution = engine.ExecuteAsync(profile, new AsyncPauseGate(), null, cancellation.Token);
        await delayStarted.Task;
        cancellation.Cancel();

        try
        {
            await execution;
            throw new InvalidOperationException("取消后应抛出 OperationCanceledException");
        }
        catch (OperationCanceledException)
        {
            TestAssert.Equal("M:10,20|C", string.Join("|", mouse.Events), "取消后不应继续点击");
        }
    }

    private static ClickPoint CreatePoint(int x, int y, int clickCount)
    {
        return new ClickPoint
        {
            X = x,
            Y = y,
            ClickCount = clickCount,
            ClickIntervalMs = 10,
            AfterDelayMs = 0,
            Enabled = true
        };
    }

    private sealed class RecordingMouseController : IMouseController
    {
        public List<string> Events { get; } = [];

        public void MoveTo(int x, int y)
        {
            Events.Add($"M:{x},{y}");
        }

        public void LeftClick()
        {
            Events.Add("C");
        }

        public void EnsureLeftButtonUp()
        {
        }
    }
}
