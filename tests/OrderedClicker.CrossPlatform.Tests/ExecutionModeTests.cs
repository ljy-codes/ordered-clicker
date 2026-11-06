using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class ExecutionModeTests
{
    public static async Task RunAsync()
    {
        await DriftPausesBeforeTheNextClick();
        await DriftBetweenPointsAlsoRequiresContinue();
        await ManualContinueRepositionsWithoutAnotherPause();
        await SafetyGuardRechecksEveryContinue();
        await GuardChangesDuringMovePreventClick();
        await StopDuringSafetyPausePreservesTheUnsentClick();
        await RehearsalVisitsEnabledPointsWithoutAnyButtonInput();
        await CancellingRehearsalNeverReleasesAButton();
        await RehearsalStopAfterMovePreservesTheVisitedPoint();
        await StepWaitsForTheWholePointAndPausesAcrossLoops();
        await StopAtAStepBoundaryPreservesTheNextPoint();
        await StepLoopBoundaryCheckpointResumesWithoutSkippingPoints();
        await CancellationAfterRepositionPreventsClick();
        await PauseDuringRepositionPreventsClick();
        await RapidRepauseKeepsWaitersPaused();
    }

    private static async Task GuardChangesDuringMovePreventClick()
    {
        var mouse = new FakeMouse();
        var gate = new AsyncPauseGate();
        var safe = true;
        var notices = 0;
        mouse.AfterMove = () => safe = false;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(Plan(), ExecutionCheckpoint.Start, gate,
                new InlineProgress(value =>
                {
                    if (!value.RequiresUserContinue)
                    {
                        return;
                    }

                    notices++;
                    TestAssert.Equal(0, mouse.ClickPositions.Count,
                        "移动期间窗口变化也必须阻止点击");
                    mouse.AfterMove = null;
                    safe = true;
                    gate.Resume();
                }), cancellation.Token,
                new ExecutionRunOptions { SafetyCheck = _ => safe ? "" : "window changed" });
        TestAssert.Equal(ExecutionOutcome.Completed, result.Outcome, "重新确认安全后应完成");
        TestAssert.Equal(1, notices, "应复核移动之后、点击之前的窗口状态");
    }

    private static async Task StopDuringSafetyPausePreservesTheUnsentClick()
    {
        var mouse = new FakeMouse();
        using var cancellation = new CancellationTokenSource();
        var stopped = await new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(Plan(), ExecutionCheckpoint.Start, new AsyncPauseGate(),
                new InlineProgress(value =>
                {
                    if (value.RequiresUserContinue)
                    {
                        cancellation.Cancel();
                    }
                }), cancellation.Token,
                new ExecutionRunOptions { SafetyCheck = _ => "blocked window" });
        TestAssert.Equal(ExecutionOutcome.Stopped, stopped.Outcome, "安全暂停不恢复也应能停止");
        TestAssert.Equal(ExecutionStage.Click, stopped.NextCheckpoint.Stage,
            "安全暂停断点应指向尚未发送的点击");
        TestAssert.Equal(0, stopped.NextCheckpoint.ClickIndex, "安全暂停不得消费点击");
        TestAssert.Equal(0L, stopped.CompletedClickCount, "安全暂停不得累计点击");
        TestAssert.Equal(0, mouse.Moves.Count, "点击前安全检查失败时也不应移动鼠标");
        var resumed = await new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(Plan(), stopped.NextCheckpoint, new AsyncPauseGate(),
                null, CancellationToken.None);
        TestAssert.Equal(ExecutionOutcome.Completed, resumed.Outcome, "安全恢复后应完成剩余点击");
        TestAssert.Equal("10,20|10,20|30,40", string.Join("|", mouse.ClickPositions),
            "恢复安全暂停断点不得丢失或重复点击");
    }

    private static async Task DriftBetweenPointsAlsoRequiresContinue()
    {
        var mouse = new FakeMouse();
        var gate = new AsyncPauseGate();
        var drifted = false;
        var notices = 0;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new ClickExecutionEngine(mouse, (_, _) =>
            {
                if (!drifted)
                {
                    drifted = true;
                    mouse.AtTarget = false;
                }

                return Task.CompletedTask;
            }).ExecuteAsync(Plan(clicks: 1), ExecutionCheckpoint.Start, gate,
                new InlineProgress(value =>
                {
                    if (value.RequiresUserContinue)
                    {
                        notices++;
                        TestAssert.Equal(1, mouse.ClickPositions.Count,
                            "点后等待期间漂移时，下一点不得先点击");
                        gate.Resume();
                    }
                }), cancellation.Token);
        TestAssert.Equal(ExecutionOutcome.Completed, result.Outcome, "确认后应执行下一点位");
        TestAssert.Equal(1, notices, "点后等待期间漂移也必须要求继续");
    }

    private static ExecutionPlan Plan(int clicks = 2, int loops = 1)
    {
        return ExecutionPlanService.Create(new ClickProfile
        {
            TotalLoops = loops,
            LoopDelayMs = 10,
            Points =
            [
                new ClickPoint
                {
                    X = 10, Y = 20, ClickCount = clicks,
                    ClickIntervalMs = 10, AfterDelayMs = 30
                },
                new ClickPoint { Enabled = false, X = 90, Y = 90 },
                new ClickPoint
                {
                    X = 30, Y = 40, ClickCount = 1,
                    ClickIntervalMs = 10, AfterDelayMs = 20
                }
            ]
        }, new ScreenBounds(0, 0, 200, 100));
    }

    private static async Task DriftPausesBeforeTheNextClick()
    {
        var mouse = new FakeMouse();
        var gate = new AsyncPauseGate();
        var paused = new TaskCompletionSource<ExecutionProgress>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        mouse.AfterClick = () => mouse.AtTarget = false;
        var progress = new InlineProgress(value =>
        {
            if (value.RequiresUserContinue)
            {
                paused.TrySetResult(value);
            }
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var execution = new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(Plan(), ExecutionCheckpoint.Start, gate, progress, cancellation.Token);
        var notice = await paused.Task.WaitAsync(cancellation.Token);
        TestAssert.Equal(1L, notice.CompletedClickCount, "漂移前只应完成一次点击");
        TestAssert.Equal(1, notice.ClickIndex, "漂移暂停必须指向未完成点击");
        TestAssert.Equal(1, mouse.ClickPositions.Count, "继续前不得自动纠偏后点击");
        TestAssert.True(gate.IsPaused && !execution.IsCompleted, "漂移必须等待用户继续");
        mouse.AfterClick = null;
        gate.Resume();
        var result = await execution.WaitAsync(cancellation.Token);
        TestAssert.Equal(ExecutionOutcome.Completed, result.Outcome, "确认漂移后应能继续");
        TestAssert.Equal("10,20|10,20|30,40", string.Join("|", mouse.ClickPositions),
            "每次点击都必须落在对应点位");
    }

    private static async Task ManualContinueRepositionsWithoutAnotherPause()
    {
        var mouse = new FakeMouse();
        var gate = new AsyncPauseGate();
        var continued = false;
        var notices = 0;
        var engine = new ClickExecutionEngine(mouse, (_, _) =>
        {
            if (!continued)
            {
                continued = true;
                gate.Pause();
                mouse.AtTarget = false;
                gate.Resume();
            }

            return Task.CompletedTask;
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await engine.ExecuteAsync(
            Plan(), ExecutionCheckpoint.Start, gate,
            new InlineProgress(value =>
            {
                if (value.RequiresUserContinue)
                {
                    notices++;
                    cancellation.Cancel();
                }
            }), cancellation.Token);
        TestAssert.Equal(ExecutionOutcome.Completed, result.Outcome,
            "主动暂停继续后应自动重建点位");
        TestAssert.Equal(0, notices, "一次主动继续不得再触发漂移暂停");
        TestAssert.Equal(3, mouse.Moves.Count, "每个实际点击都必须重新定位");
    }

    private static async Task SafetyGuardRechecksEveryContinue()
    {
        var mouse = new FakeMouse();
        var gate = new AsyncPauseGate();
        var attempts = 0;
        var notices = 0;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(Plan(), ExecutionCheckpoint.Start, gate,
                new InlineProgress(value =>
                {
                    if (!value.RequiresUserContinue)
                    {
                        return;
                    }

                    notices++;
                    TestAssert.Equal(0, mouse.ClickPositions.Count, "窗口不安全时不得点击");
                    TestAssert.True(value.Message.Contains("window", StringComparison.Ordinal),
                        "应显示安全检查返回的原因");
                    gate.Resume();
                }), cancellation.Token, new ExecutionRunOptions
                {
                    SafetyCheck = _ => ++attempts <= 2 ? "wrong window" : null
                });
        TestAssert.Equal(ExecutionOutcome.Completed, result.Outcome, "安全检查通过后应完成");
        TestAssert.Equal(2, notices, "继续时仍不安全必须再次暂停");
    }

    private static async Task RehearsalVisitsEnabledPointsWithoutAnyButtonInput()
    {
        var mouse = new FakeMouse();
        var sampler = new CountingSampler();
        var plan = Plan(clicks: 5, loops: 2);
        plan.ScreenStability.Enabled = true;
        var reports = new List<ExecutionProgress>();
        var suppliedCheckpoint = new ExecutionCheckpoint(0, 0, 1, ExecutionStage.Click, 0, 1);
        var result = await new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask,
                new ScreenStabilityDetector(sampler, (_, _) => Task.CompletedTask))
            .ExecuteAsync(plan, suppliedCheckpoint, new AsyncPauseGate(),
                new InlineProgress(reports.Add), CancellationToken.None,
                new ExecutionRunOptions { Mode = ExecutionRunMode.Rehearsal });

        TestAssert.Equal(ExecutionOutcome.Completed, result.Outcome, "演练应按访问点位完成");
        TestAssert.Equal("10,20|30,40|10,20|30,40", string.Join("|", mouse.Moves),
            "演练应独立从头开始，每轮只移动一次到每个启用点位");
        TestAssert.Equal(4L, result.CompletedPointExecutionCount, "演练统计访问点次");
        TestAssert.Equal(0L, result.CompletedClickCount, "演练实际点击数必须为零");
        TestAssert.Equal(0L, result.PlannedClickCount, "演练不得使用正常点击数显示进度");
        TestAssert.Equal(0L, result.NextCheckpoint.CompletedClickCount, "演练断点不得携带正常计数");
        TestAssert.Equal(1L, suppliedCheckpoint.CompletedClickCount, "传入正常断点必须保持不变");
        TestAssert.Equal(0, mouse.ClickPositions.Count, "演练不得点击");
        TestAssert.Equal(0, mouse.Releases, "演练不得在 finally 清理鼠标按键");
        TestAssert.Equal(0, sampler.Samples, "演练应跳过稳定检测");
        TestAssert.True(reports.Count > 0 && reports.All(value =>
                value.CompletedClickCount == 0 && value.PlannedClickCount == 0
                && value.ClickCount == 0 && value.ClickIndex == 0),
            "演练进度应始终显示零实际点击");
        TestAssert.Equal(4L, reports[^1].CompletedPointExecutionCount, "末次进度应包含所有访问点位");
        var formalMouse = new FakeMouse();
        var formal = await new ClickExecutionEngine(formalMouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(plan, suppliedCheckpoint, new AsyncPauseGate(),
                null, CancellationToken.None);
        TestAssert.Equal(ExecutionOutcome.Completed, formal.Outcome, "演练后原正式断点应仍可续跑");
        TestAssert.Equal(12L, formal.CompletedClickCount, "正式结果应保留原点击计数");
        TestAssert.Equal(11, formalMouse.ClickPositions.Count, "正式续跑不得重放原先已完成的一次点击");
    }

    private static async Task CancellingRehearsalNeverReleasesAButton()
    {
        var mouse = new FakeMouse();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new ClickExecutionEngine(mouse).ExecuteAsync(
            Plan(), ExecutionCheckpoint.Start, new AsyncPauseGate(), null,
            cancellation.Token, new ExecutionRunOptions { Mode = ExecutionRunMode.Rehearsal });
        TestAssert.Equal(ExecutionOutcome.Stopped, result.Outcome, "演练可立即停止");
        TestAssert.Equal(0, mouse.Releases, "取消演练也不得发送松键");
    }

    private static async Task RehearsalStopAfterMovePreservesTheVisitedPoint()
    {
        using var cancellation = new CancellationTokenSource();
        var mouse = new FakeMouse { AfterMove = cancellation.Cancel };
        var result = await new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(Plan(), ExecutionCheckpoint.Start, new AsyncPauseGate(), null,
                cancellation.Token, new ExecutionRunOptions { Mode = ExecutionRunMode.Rehearsal });
        TestAssert.Equal(ExecutionOutcome.Stopped, result.Outcome, "演练在移动后应可立即停止");
        TestAssert.Equal(1L, result.CompletedPointExecutionCount, "演练停止也必须准确统计已访问点位");
        TestAssert.Equal(1, mouse.Moves.Count, "每个演练点只移动一次");
        TestAssert.Equal(0, mouse.Releases, "演练停止不得发送清理松键");
    }

    private static async Task StepWaitsForTheWholePointAndPausesAcrossLoops()
    {
        var mouse = new FakeMouse();
        var gate = new AsyncPauseGate();
        var waited = 0;
        var notices = 0;
        var sampler = new CountingSampler();
        var plan = Plan(loops: 2);
        plan.ScreenStability.Enabled = true;
        plan.ScreenStability.SampleIntervalMs = 1;
        plan.ScreenStability.StableDurationMs = 1;
        var engine = new ClickExecutionEngine(mouse,
            (duration, _) =>
            {
                waited += (int)duration.TotalMilliseconds;
                return Task.CompletedTask;
            }, new ScreenStabilityDetector(sampler, (_, _) => Task.CompletedTask));
        var expectedWaits = new[] { 40, 60, 110 };
        var expectedClicks = new[] { 2, 3, 5 };
        var expectedSamples = new[] { 2, 4, 6 };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await engine.ExecuteAsync(
            plan, ExecutionCheckpoint.Start, gate,
            new InlineProgress(value =>
            {
                if (!value.RequiresUserContinue)
                {
                    return;
                }

                TestAssert.True(gate.IsPaused, "单步边界必须处于暂停状态");
                TestAssert.Equal(expectedWaits[notices], waited, "单步应包含点内间隔和点后等待");
                TestAssert.Equal(expectedClicks[notices], mouse.ClickPositions.Count, "单步必须完成整个点位");
                TestAssert.Equal(expectedSamples[notices], sampler.Samples, "单步必须完成画面稳定检查");
                TestAssert.Equal((long)notices + 1, value.CompletedPointExecutionCount,
                    "单步边界断点必须保留已完成点次");
                notices++;
                gate.Resume();
            }), cancellation.Token, new ExecutionRunOptions { Mode = ExecutionRunMode.Step });
        TestAssert.Equal(ExecutionOutcome.Completed, result.Outcome, "最后一个点应直接完成");
        TestAssert.Equal(3, notices, "四个点次只应在前三个边界暂停");
        TestAssert.Equal(6L, result.CompletedClickCount, "单步正常累计全部点击");
    }

    private static async Task StopAtAStepBoundaryPreservesTheNextPoint()
    {
        var mouse = new FakeMouse();
        var gate = new AsyncPauseGate();
        using var cancellation = new CancellationTokenSource();
        var stopped = await new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(Plan(), ExecutionCheckpoint.Start, gate,
                new InlineProgress(value =>
                {
                    if (value.RequiresUserContinue)
                    {
                        cancellation.Cancel();
                    }
                }), cancellation.Token, new ExecutionRunOptions { Mode = ExecutionRunMode.Step });
        TestAssert.Equal(ExecutionOutcome.Stopped, stopped.Outcome, "单步暂停时应能停止");
        TestAssert.Equal(1, stopped.NextCheckpoint.PointIndex, "断点应指向下一个未执行点位");
        TestAssert.Equal(ExecutionStage.Move, stopped.NextCheckpoint.Stage, "恢复应从下一个点位移动开始");
        TestAssert.Equal(2L, stopped.CompletedClickCount, "停止前点击计数应准确");
    }

    private static async Task StepLoopBoundaryCheckpointResumesWithoutSkippingPoints()
    {
        var plan = Plan(clicks: 1, loops: 2);
        var gate = new AsyncPauseGate();
        using var cancellation = new CancellationTokenSource();
        ExecutionCheckpoint? reportedCheckpoint = null;
        var stopped = await new ClickExecutionEngine(new FakeMouse(), (_, _) => Task.CompletedTask)
            .ExecuteAsync(plan, ExecutionCheckpoint.Start, gate,
                new InlineProgress(value =>
                {
                    if (!value.RequiresUserContinue)
                    {
                        return;
                    }

                    if (value.CompletedPointExecutionCount == 1)
                    {
                        gate.Resume();
                        return;
                    }

                    reportedCheckpoint = new ExecutionCheckpoint(
                        value.CurrentLoop - 1, value.PointIndex - 1, value.ClickIndex,
                        value.Stage, value.CompletedPointExecutionCount, value.CompletedClickCount);
                    cancellation.Cancel();
                }), cancellation.Token, new ExecutionRunOptions { Mode = ExecutionRunMode.Step });
        TestAssert.Equal(ExecutionOutcome.Stopped, stopped.Outcome, "跨循环单步暂停时应能停止");
        TestAssert.Equal(new ExecutionCheckpoint(0, 1, 1, ExecutionStage.LoopDelay, 2, 2),
            stopped.NextCheckpoint, "循环边界必须保存待完成的轮间等待及正确循环序号");
        TestAssert.Equal(stopped.NextCheckpoint, reportedCheckpoint,
            "UI 根据进度构造的断点必须与引擎结果一致");
        var mouse = new FakeMouse();
        var resumedGate = new AsyncPauseGate();
        var pauses = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var resumed = await new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(plan, stopped.NextCheckpoint, resumedGate,
                new InlineProgress(value =>
                {
                    if (value.RequiresUserContinue)
                    {
                        pauses++;
                        resumedGate.Resume();
                    }
                }), timeout.Token, new ExecutionRunOptions { Mode = ExecutionRunMode.Step });
        TestAssert.Equal(ExecutionOutcome.Completed, resumed.Outcome, "跨循环断点恢复后应完成");
        TestAssert.Equal("10,20|30,40", string.Join("|", mouse.ClickPositions),
            "恢复后必须从第二轮首点开始，不跳点也不重放上一轮");
        TestAssert.Equal(4L, resumed.CompletedClickCount, "跨循环续跑的累计点击应准确");
        TestAssert.Equal(1, pauses, "恢复后的末点应直接完成");
    }

    private static async Task CancellationAfterRepositionPreventsClick()
    {
        using var cancellation = new CancellationTokenSource();
        var mouse = new FakeMouse { AfterMove = cancellation.Cancel };
        var result = await new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(Plan(), ExecutionCheckpoint.Start, new AsyncPauseGate(),
                null, cancellation.Token);
        TestAssert.Equal(ExecutionOutcome.Stopped, result.Outcome, "定位后停止必须生效");
        TestAssert.Equal(0, mouse.ClickPositions.Count, "定位与点击之间取消不得继续点击");
        TestAssert.Equal(0L, result.CompletedClickCount, "未发送点击不得消费点击计数");
    }

    private static async Task PauseDuringRepositionPreventsClick()
    {
        var gate = new AsyncPauseGate();
        var mouse = new FakeMouse { AfterMove = gate.Pause };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var execution = new ClickExecutionEngine(mouse, (_, _) => Task.CompletedTask)
            .ExecuteAsync(Plan(), ExecutionCheckpoint.Start, gate, null, cancellation.Token);
        TestAssert.True(gate.IsPaused, "移动时应允许暂停");
        TestAssert.Equal(0, mouse.ClickPositions.Count, "移动与点击之间暂停不得发送点击");
        mouse.AfterMove = null;
        gate.Resume();
        var result = await execution.WaitAsync(cancellation.Token);
        TestAssert.Equal(ExecutionOutcome.Completed, result.Outcome, "继续后应重新定位并完成");
    }

    private static async Task RapidRepauseKeepsWaitersPaused()
    {
        var gate = new AsyncPauseGate();
        using var cancellation = new CancellationTokenSource();
        var schedulers = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1);
        var wait = await Task.Factory.StartNew(() =>
        {
            gate.Pause();
            var pending = gate.WaitIfPausedAsync(cancellation.Token);
            gate.Resume();
            gate.Pause();
            return pending;
        }, CancellationToken.None, TaskCreationOptions.None, schedulers.ExclusiveScheduler);
        await Task.Factory.StartNew(() => { }, CancellationToken.None,
            TaskCreationOptions.None, schedulers.ExclusiveScheduler);
        TestAssert.True(!wait.IsCompleted, "快速继续再暂停不得放行旧等待者");
        cancellation.Cancel();
        try
        {
            await wait;
            throw new InvalidOperationException("暂停等待必须响应取消");
        }
        catch (OperationCanceledException)
        {
        }

        schedulers.Complete();
    }

    private sealed class InlineProgress(Action<ExecutionProgress> report) : IProgress<ExecutionProgress>
    {
        public void Report(ExecutionProgress value) => report(value);
    }

    private sealed class FakeMouse : IMouseController
    {
        public List<string> Moves { get; } = [];
        public List<string> ClickPositions { get; } = [];
        public bool AtTarget { get; set; } = true;
        public int Releases { get; private set; }
        public Action? AfterMove { get; set; }
        public Action? AfterClick { get; set; }
        private string _position = "wrong";

        public void MoveTo(int x, int y)
        {
            _position = $"{x},{y}";
            Moves.Add(_position);
            AtTarget = true;
            AfterMove?.Invoke();
        }

        public bool IsAtPosition(int x, int y) => AtTarget && _position == $"{x},{y}";

        public void LeftClick()
        {
            ClickPositions.Add(AtTarget ? _position : "wrong");
            AfterClick?.Invoke();
        }

        public void EnsureLeftButtonUp() => Releases++;
    }

    private sealed class CountingSampler : IScreenSampler
    {
        public int Samples { get; private set; }
        public Task<byte[]> SampleAsync(ScreenBounds region, CancellationToken cancellationToken)
        {
            Samples++;
            return Task.FromResult<byte[]>([0]);
        }
    }
}
