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
        await ResumesIncompleteAfterPointDelay();
        await PauseFreezesDelayConsumption();
        await DoesNotRepeatIndeterminateClick();
    }

    private static async Task ResumesIncompleteAfterPointDelay()
    {
        using var cancellation = new CancellationTokenSource();
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mouse = new RecordingMouseController();
        var engine = new ClickExecutionEngine(mouse, async (_, token) =>
        {
            delayStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var profile = new ClickProfile
        {
            TotalLoops = 1,
            Points = [CreatePoint(10, 20, 1)]
        };
        profile.Points[0].AfterDelayMs = 100;
        var plan = ExecutionPlanService.Create(
            profile,
            new ScreenBounds(0, 0, 1920, 1080));

        var execution = engine.ExecuteAsync(
            plan,
            ExecutionCheckpoint.Start,
            new AsyncPauseGate(),
            null,
            cancellation.Token);
        await delayStarted.Task;
        cancellation.Cancel();
        var stopped = await execution;

        TestAssert.Equal(ExecutionStage.AfterPointDelay, stopped.NextCheckpoint.Stage,
            "点后等待未完成时断点必须停留在该阶段");

        var resumedDelays = 0;
        var resumedMouse = new RecordingMouseController();
        var resumed = await new ClickExecutionEngine(
            resumedMouse,
            (_, _) =>
            {
                resumedDelays++;
                return Task.CompletedTask;
            }).ExecuteAsync(
                plan,
                stopped.NextCheckpoint,
                new AsyncPauseGate(),
                null,
                CancellationToken.None);

        TestAssert.Equal(ExecutionOutcome.Completed, resumed.Outcome,
            "补完点后等待后应完成");
        TestAssert.Equal(0, resumedMouse.Events.Count(item => item == "C"),
            "恢复未完成等待时不得重复已经完成的点击");
        TestAssert.True(resumedDelays > 0, "恢复时必须重新执行未完成的点后等待");
    }

    private static async Task PauseFreezesDelayConsumption()
    {
        var pauseGate = new AsyncPauseGate();
        var firstSlice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slices = 0;
        var delay = new PausableDelay(async (_, token) =>
        {
            slices++;
            firstSlice.TrySetResult();
            await releaseSlice.Task.WaitAsync(token);
        }, sliceMilliseconds: 25);

        var wait = delay.WaitAsync(50, pauseGate, CancellationToken.None);
        await firstSlice.Task;
        pauseGate.Pause();
        releaseSlice.TrySetResult();
        await Task.Delay(30);

        TestAssert.True(!wait.IsCompleted, "暂停期间不应消耗剩余等待时间");

        pauseGate.Resume();
        await wait;
        TestAssert.Equal(2, slices, "恢复后应继续完成剩余延时分片");
    }

    private static async Task DoesNotRepeatIndeterminateClick()
    {
        var profile = new ClickProfile
        {
            Points = [CreatePoint(10, 20, 2)]
        };
        var plan = ExecutionPlanService.Create(
            profile,
            new ScreenBounds(0, 0, 1920, 1080));
        var failed = await new ClickExecutionEngine(
            new IndeterminateMouseController(),
            (_, _) => Task.CompletedTask).ExecuteAsync(
                plan,
                ExecutionCheckpoint.Start,
                new AsyncPauseGate(),
                null,
                CancellationToken.None);

        TestAssert.Equal(ExecutionOutcome.Failed, failed.Outcome,
            "点击结果不确定时应停止并要求用户检查");
        TestAssert.Equal(1L, failed.CompletedClickCount,
            "结果不确定的点击应标记为已消费，避免自动重复");
        TestAssert.Equal(ExecutionStage.ClickInterval, failed.NextCheckpoint.Stage,
            "断点应从结果不确定点击之后继续");

        var resumedMouse = new RecordingMouseController();
        var resumed = await new ClickExecutionEngine(
            resumedMouse,
            (_, _) => Task.CompletedTask).ExecuteAsync(
                plan,
                failed.NextCheckpoint,
                new AsyncPauseGate(),
                null,
                CancellationToken.None);

        TestAssert.Equal(ExecutionOutcome.Completed, resumed.Outcome,
            "用户确认后应能从下一次点击继续");
        TestAssert.Equal(1, resumedMouse.Events.Count(item => item == "C"),
            "续跑不得重复结果不确定的点击");
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

    private sealed class IndeterminateMouseController : IMouseController
    {
        public void MoveTo(int x, int y)
        {
        }

        public void LeftClick()
        {
            throw new IndeterminateClickException("点击结果不确定");
        }

        public void EnsureLeftButtonUp()
        {
        }
    }
}
