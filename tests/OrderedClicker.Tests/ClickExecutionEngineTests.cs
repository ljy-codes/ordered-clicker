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
        await ExecutesFiveHundredPointsAcrossThreeLoops();
        await ResumesFromTheNextUnfinishedClick();
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

    private static async Task ExecutesFiveHundredPointsAcrossThreeLoops()
    {
        var mouse = new RecordingMouseController();
        var engine = new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask);
        var profile = new ClickProfile
        {
            TotalLoops = 3,
            LoopDelayMs = 0,
            Points = Enumerable.Range(0, 500)
                .Select(index => CreatePoint(index, index + 1, 1))
                .ToList()
        };
        var plan = ExecutionPlanService.Create(
            profile,
            new ScreenBounds(0, 0, 1920, 1080));

        var result = await engine.ExecuteAsync(
            plan,
            ExecutionCheckpoint.Start,
            new AsyncPauseGate(),
            null,
            CancellationToken.None);

        TestAssert.Equal(ExecutionOutcome.Completed, result.Outcome,
            "完整执行后结果应为完成");
        TestAssert.Equal(1500L, result.CompletedPointExecutionCount,
            "500 个点执行 3 轮必须完成 1500 个点次");
        TestAssert.Equal(1500L, result.CompletedClickCount,
            "每点一次时点击总数必须完整");
        TestAssert.Equal(3000, mouse.Events.Count,
            "每个点都应产生一次移动和一次点击");
    }

    private static async Task ResumesFromTheNextUnfinishedClick()
    {
        using var cancellation = new CancellationTokenSource();
        var firstMouse = new RecordingMouseController(() =>
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            cancellation.Cancel();
        }, cancelAfterClickCount: 2);
        var engine = new ClickExecutionEngine(firstMouse, (_, _) => Task.CompletedTask);
        var profile = new ClickProfile
        {
            TotalLoops = 1,
            LoopDelayMs = 0,
            Points = [CreatePoint(10, 20, 5), CreatePoint(30, 40, 1)]
        };
        var plan = ExecutionPlanService.Create(
            profile,
            new ScreenBounds(0, 0, 1920, 1080));

        var stopped = await engine.ExecuteAsync(
            plan,
            ExecutionCheckpoint.Start,
            new AsyncPauseGate(),
            null,
            cancellation.Token);

        TestAssert.Equal(ExecutionOutcome.Stopped, stopped.Outcome,
            "用户停止后应返回可恢复结果");
        TestAssert.Equal(2L, stopped.CompletedClickCount,
            "停止前完成的点击数必须准确");
        TestAssert.Equal(2, stopped.NextCheckpoint.ClickIndex,
            "断点应指向同一点位的第三次点击");

        var resumedMouse = new RecordingMouseController();
        var resumed = await new ClickExecutionEngine(
            resumedMouse,
            (_, _) => Task.CompletedTask).ExecuteAsync(
                plan,
                stopped.NextCheckpoint,
                new AsyncPauseGate(),
                null,
                CancellationToken.None);

        TestAssert.Equal(ExecutionOutcome.Completed, resumed.Outcome,
            "从断点继续后应完整完成");
        TestAssert.Equal(6L, resumed.CompletedClickCount,
            "恢复结果应累计停止前已完成的点击");
        TestAssert.Equal(4, resumedMouse.Events.Count(item => item == "C"),
            "恢复时不得重复前两次点击");
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
        private readonly Action? _afterClick;
        private readonly int _cancelAfterClickCount;
        private int _clickCount;

        public RecordingMouseController(
            Action? afterClick = null,
            int cancelAfterClickCount = int.MaxValue)
        {
            _afterClick = afterClick;
            _cancelAfterClickCount = cancelAfterClickCount;
        }

        public List<string> Events { get; } = [];

        public void MoveTo(int x, int y)
        {
            Events.Add($"M:{x},{y}");
        }

        public void LeftClick()
        {
            Events.Add("C");
            _clickCount++;
            if (_clickCount == _cancelAfterClickCount)
            {
                _afterClick?.Invoke();
            }
        }

        public void EnsureLeftButtonUp()
        {
        }
    }
}
