using System.Diagnostics;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Core;

public sealed class ClickExecutionEngine
{
    private readonly IMouseController _mouseController;
    private readonly PausableDelay _pausableDelay;
    private readonly ScreenStabilityDetector? _stabilityDetector;

    public ClickExecutionEngine(
        IMouseController mouseController,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ScreenStabilityDetector? stabilityDetector = null)
    {
        _mouseController = mouseController;
        _pausableDelay = new PausableDelay(delay);
        _stabilityDetector = stabilityDetector;
    }

    public async Task ExecuteAsync(
        ClickProfile profile,
        AsyncPauseGate pauseGate,
        IProgress<ExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bounds = SystemInformation.VirtualScreen;
        var plan = ExecutionPlanService.Create(
            profile,
            new ScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        var result = await ExecuteAsync(
            plan,
            ExecutionCheckpoint.Start,
            pauseGate,
            progress,
            cancellationToken);

        if (result.Outcome == ExecutionOutcome.Stopped)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.Outcome == ExecutionOutcome.Failed)
        {
            throw new InvalidOperationException(result.Message);
        }
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        ExecutionCheckpoint checkpoint,
        AsyncPauseGate pauseGate,
        IProgress<ExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(pauseGate);
        var stopwatch = Stopwatch.StartNew();
        var completedPoints = checkpoint.CompletedPointExecutionCount;
        var completedClicks = checkpoint.CompletedClickCount;
        var nextCheckpoint = checkpoint;
        int? lastSourceIndex = null;

        try
        {
            ValidateCheckpoint(plan, nextCheckpoint);
            while (nextCheckpoint.Stage != ExecutionStage.Completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var point = plan.Points[nextCheckpoint.PointIndex];

                switch (nextCheckpoint.Stage)
                {
                    case ExecutionStage.Move:
                        await pauseGate.WaitIfPausedAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        _mouseController.MoveTo(point.X, point.Y, plan.VirtualScreen);
                        nextCheckpoint = nextCheckpoint with
                        {
                            Stage = ExecutionStage.Click
                        };
                        break;

                    case ExecutionStage.Click:
                        await pauseGate.WaitIfPausedAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        IndeterminateClickException? indeterminateClick = null;
                        try
                        {
                            _mouseController.LeftClick();
                        }
                        catch (IndeterminateClickException exception)
                        {
                            indeterminateClick = exception;
                        }

                        completedClicks++;
                        lastSourceIndex = point.SourceIndex;
                        var pointFinished =
                            nextCheckpoint.ClickIndex + 1 == point.ClickCount;
                        if (pointFinished)
                        {
                            completedPoints++;
                            nextCheckpoint = nextCheckpoint with
                            {
                                ClickIndex = point.ClickCount,
                                Stage = ExecutionStage.AfterPointDelay,
                                CompletedPointExecutionCount = completedPoints,
                                CompletedClickCount = completedClicks
                            };
                        }
                        else
                        {
                            nextCheckpoint = nextCheckpoint with
                            {
                                ClickIndex = nextCheckpoint.ClickIndex + 1,
                                Stage = ExecutionStage.ClickInterval,
                                CompletedPointExecutionCount = completedPoints,
                                CompletedClickCount = completedClicks
                            };
                        }

                        ReportProgress(
                            plan,
                            nextCheckpoint,
                            point,
                            completedPoints,
                            completedClicks,
                            progress);
                        if (indeterminateClick is not null)
                        {
                            throw indeterminateClick;
                        }

                        break;

                    case ExecutionStage.ClickInterval:
                        await _pausableDelay.WaitAsync(
                            point.ClickIntervalMs,
                            pauseGate,
                            cancellationToken);
                        nextCheckpoint = nextCheckpoint with
                        {
                            Stage = ExecutionStage.Click
                        };
                        break;

                    case ExecutionStage.AfterPointDelay:
                        await _pausableDelay.WaitAsync(
                            point.AfterDelayMs,
                            pauseGate,
                            cancellationToken);
                        nextCheckpoint = nextCheckpoint with
                        {
                            Stage = ExecutionStage.StabilityCheck
                        };
                        break;

                    case ExecutionStage.StabilityCheck:
                        if (plan.ScreenStability.Enabled
                            && _stabilityDetector is not null)
                        {
                            var stability = await _stabilityDetector.WaitForStableAsync(
                                plan.StabilityRegion,
                                plan.ScreenStability,
                                pauseGate,
                                cancellationToken);
                            if (stability == ScreenStabilityResult.TimedOut)
                            {
                                pauseGate.Pause();
                                ReportProgress(
                                    plan,
                                    nextCheckpoint,
                                    point,
                                    completedPoints,
                                    completedClicks,
                                    progress,
                                    true,
                                    $"点位 {point.SourceIndex + 1} 后画面未稳定");
                                await pauseGate.WaitIfPausedAsync(cancellationToken);
                            }
                        }

                        nextCheckpoint = AdvanceAfterPoint(plan, nextCheckpoint);
                        break;

                    case ExecutionStage.LoopDelay:
                        await _pausableDelay.WaitAsync(
                            plan.LoopDelayMs,
                            pauseGate,
                            cancellationToken);
                        nextCheckpoint = new ExecutionCheckpoint(
                            nextCheckpoint.LoopIndex + 1,
                            0,
                            0,
                            ExecutionStage.Move,
                            completedPoints,
                            completedClicks);
                        break;

                    default:
                        throw new InvalidOperationException("执行断点阶段无效。");
                }
            }

            var completed =
                completedPoints == plan.PlannedPointExecutionCount
                && completedClicks == plan.PlannedClickCount;
            return CreateResult(
                completed ? ExecutionOutcome.Completed : ExecutionOutcome.Failed,
                completed
                    ? "全部计划点位、等待阶段和点击次数已完成。"
                    : "执行计数与计划不一致，未标记为完成。",
                plan,
                completedPoints,
                completedClicks,
                lastSourceIndex,
                nextCheckpoint,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return CreateResult(
                ExecutionOutcome.Stopped,
                "执行已停止，可从断点继续。",
                plan,
                completedPoints,
                completedClicks,
                lastSourceIndex,
                nextCheckpoint,
                stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            return CreateResult(
                ExecutionOutcome.Failed,
                exception.Message,
                plan,
                completedPoints,
                completedClicks,
                lastSourceIndex,
                nextCheckpoint,
                stopwatch.Elapsed);
        }
        finally
        {
            _mouseController.EnsureLeftButtonUp();
        }
    }

    private static ExecutionCheckpoint AdvanceAfterPoint(
        ExecutionPlan plan,
        ExecutionCheckpoint checkpoint)
    {
        if (checkpoint.PointIndex + 1 < plan.Points.Count)
        {
            return new ExecutionCheckpoint(
                checkpoint.LoopIndex,
                checkpoint.PointIndex + 1,
                0,
                ExecutionStage.Move,
                checkpoint.CompletedPointExecutionCount,
                checkpoint.CompletedClickCount);
        }

        if (checkpoint.LoopIndex + 1 < plan.TotalLoops)
        {
            return checkpoint with
            {
                Stage = ExecutionStage.LoopDelay
            };
        }

        return new ExecutionCheckpoint(
            plan.TotalLoops,
            0,
            0,
            ExecutionStage.Completed,
            checkpoint.CompletedPointExecutionCount,
            checkpoint.CompletedClickCount);
    }

    private static void ValidateCheckpoint(
        ExecutionPlan plan,
        ExecutionCheckpoint checkpoint)
    {
        if (plan.Points.Count == 0)
        {
            throw new InvalidOperationException("执行计划没有可执行点位。");
        }

        if (checkpoint.Stage == ExecutionStage.Completed)
        {
            if (checkpoint.LoopIndex != plan.TotalLoops)
            {
                throw new InvalidOperationException("完成断点的循环序号无效。");
            }

            return;
        }

        if (checkpoint.LoopIndex < 0
            || checkpoint.LoopIndex >= plan.TotalLoops
            || checkpoint.PointIndex < 0
            || checkpoint.PointIndex >= plan.Points.Count
            || checkpoint.ClickIndex < 0
            || checkpoint.ClickIndex > plan.Points[checkpoint.PointIndex].ClickCount)
        {
            throw new InvalidOperationException("执行断点无效。");
        }

        if (checkpoint.Stage is ExecutionStage.Move or ExecutionStage.Click
            && checkpoint.ClickIndex >= plan.Points[checkpoint.PointIndex].ClickCount)
        {
            throw new InvalidOperationException("执行断点的点击序号无效。");
        }
    }

    private static void ReportProgress(
        ExecutionPlan plan,
        ExecutionCheckpoint checkpoint,
        ExecutionPlanPoint point,
        long completedPoints,
        long completedClicks,
        IProgress<ExecutionProgress>? progress,
        bool requiresUserContinue = false,
        string message = "")
    {
        var completedClickInPoint = Math.Min(
            checkpoint.ClickIndex,
            point.ClickCount);
        progress?.Report(new ExecutionProgress(
            checkpoint.LoopIndex + 1,
            plan.TotalLoops,
            checkpoint.PointIndex + 1,
            plan.Points.Count,
            completedClickInPoint,
            point.ClickCount)
        {
            SourceIndex = point.SourceIndex,
            CompletedPointExecutionCount = completedPoints,
            PlannedPointExecutionCount = plan.PlannedPointExecutionCount,
            CompletedClickCount = completedClicks,
            PlannedClickCount = plan.PlannedClickCount,
            Stage = checkpoint.Stage,
            RequiresUserContinue = requiresUserContinue,
            Message = message
        });
    }

    private static ExecutionResult CreateResult(
        ExecutionOutcome outcome,
        string message,
        ExecutionPlan plan,
        long completedPoints,
        long completedClicks,
        int? lastSourceIndex,
        ExecutionCheckpoint checkpoint,
        TimeSpan elapsed)
    {
        return new ExecutionResult(
            outcome,
            plan.PlannedPointExecutionCount,
            completedPoints,
            plan.PlannedClickCount,
            completedClicks,
            lastSourceIndex,
            checkpoint,
            message,
            elapsed);
    }
}
