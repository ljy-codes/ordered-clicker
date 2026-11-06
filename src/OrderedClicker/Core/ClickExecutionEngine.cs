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

#if WINDOWS
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

#endif

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        ExecutionCheckpoint checkpoint,
        AsyncPauseGate pauseGate,
        IProgress<ExecutionProgress>? progress,
        CancellationToken cancellationToken,
        ExecutionRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(pauseGate);
        var mode = options?.Mode ?? ExecutionRunMode.Normal;
        var rehearsal = mode == ExecutionRunMode.Rehearsal;
        var safetyCheck = options?.SafetyCheck;
        // A rehearsal has its own visit counters; a supplied normal checkpoint is untouched.
        var startCheckpoint = rehearsal ? ExecutionCheckpoint.Start : checkpoint;
        var stopwatch = Stopwatch.StartNew();
        var completedPoints = startCheckpoint.CompletedPointExecutionCount;
        var completedClicks = startCheckpoint.CompletedClickCount;
        var nextCheckpoint = startCheckpoint;
        int? lastSourceIndex = null;
        var positionEstablished = false;
        var positionedGeneration = pauseGate.ResumeGeneration;
        var positionedX = 0;
        var positionedY = 0;

        async Task PauseForContinueAsync(ExecutionPlanPoint point, string message)
        {
            pauseGate.Pause();
            ReportProgress(
                plan, nextCheckpoint, point, completedPoints, completedClicks,
                progress, true, message, rehearsal);
            await pauseGate.WaitIfPausedAsync(cancellationToken);
            positionEstablished = false;
        }

        async Task PrepareInputAsync(ExecutionPlanPoint point)
        {
            while (true)
            {
                await pauseGate.WaitIfPausedAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var generation = pauseGate.ResumeGeneration;
                if (generation != positionedGeneration)
                {
                    positionEstablished = false;
                }

                if (positionEstablished && !_mouseController.IsAtPosition(positionedX, positionedY))
                {
                    await PauseForContinueAsync(point, $"点位 {point.SourceIndex + 1} 的鼠标位置发生偏移");
                    continue;
                }

                var reason = safetyCheck?.Invoke(point);
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(reason))
                {
                    await PauseForContinueAsync(point, reason);
                    continue;
                }

                if (pauseGate.IsPaused || generation != pauseGate.ResumeGeneration)
                {
                    continue;
                }

                _mouseController.MoveTo(point.X, point.Y, plan.VirtualScreen);
                positionEstablished = true;
                positionedGeneration = generation;
                positionedX = point.X;
                positionedY = point.Y;
                if (rehearsal)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (pauseGate.IsPaused || generation != pauseGate.ResumeGeneration)
                {
                    continue;
                }

                reason = safetyCheck?.Invoke(point);
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(reason))
                {
                    await PauseForContinueAsync(point, reason);
                    continue;
                }

                if (!_mouseController.IsAtPosition(point.X, point.Y))
                {
                    await PauseForContinueAsync(point, $"点位 {point.SourceIndex + 1} 的鼠标定位未保持");
                    continue;
                }

                if (!pauseGate.IsPaused && generation == pauseGate.ResumeGeneration)
                {
                    return;
                }
            }
        }

        try
        {
            if (!Enum.IsDefined(mode))
            {
                throw new ArgumentOutOfRangeException(nameof(options), "执行模式无效。");
            }

            ValidateCheckpoint(plan, nextCheckpoint);
            while (nextCheckpoint.Stage != ExecutionStage.Completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var point = plan.Points[nextCheckpoint.PointIndex];

                switch (nextCheckpoint.Stage)
                {
                    case ExecutionStage.Move:
                        if (rehearsal)
                        {
                            await PrepareInputAsync(point);
                            completedPoints++;
                            lastSourceIndex = point.SourceIndex;
                            nextCheckpoint = nextCheckpoint with
                            {
                                Stage = ExecutionStage.AfterPointDelay,
                                CompletedPointExecutionCount = completedPoints
                            };
                            ReportProgress(
                                plan, nextCheckpoint, point, completedPoints, completedClicks,
                                progress, rehearsal: true);
                            break;
                        }

                        nextCheckpoint = nextCheckpoint with
                        {
                            Stage = ExecutionStage.Click
                        };
                        break;

                    case ExecutionStage.Click:
                        await PrepareInputAsync(point);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (pauseGate.IsPaused || positionedGeneration != pauseGate.ResumeGeneration)
                        {
                            break;
                        }

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
                        await pauseGate.WaitIfPausedAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!rehearsal && plan.ScreenStability.Enabled
                            && _stabilityDetector is not null)
                        {
                            var stability = await _stabilityDetector.WaitForStableAsync(
                                plan.StabilityRegion,
                                plan.ScreenStability,
                                pauseGate,
                                cancellationToken);
                            if (stability == ScreenStabilityResult.TimedOut)
                            {
                                await PauseForContinueAsync(
                                    point, $"点位 {point.SourceIndex + 1} 后画面未稳定");
                            }
                        }

                        nextCheckpoint = AdvanceAfterPoint(plan, nextCheckpoint);
                        if (mode == ExecutionRunMode.Step
                            && nextCheckpoint.Stage != ExecutionStage.Completed)
                        {
                            await PauseForContinueAsync(
                                plan.Points[nextCheckpoint.PointIndex],
                                "当前点位已完成，请继续执行下一点位");
                        }

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
                && completedClicks == (rehearsal ? 0 : plan.PlannedClickCount);
            var completionMessage = rehearsal
                ? "全部计划点位演练已完成，未发送点击。"
                : "全部计划点位、等待阶段和点击次数已完成。";
            return CreateResult(
                completed ? ExecutionOutcome.Completed : ExecutionOutcome.Failed,
                completed
                    ? completionMessage
                    : "执行计数与计划不一致，未标记为完成。",
                plan,
                completedPoints,
                completedClicks,
                lastSourceIndex,
                nextCheckpoint,
                stopwatch.Elapsed,
                rehearsal);
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
                stopwatch.Elapsed,
                rehearsal);
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
                stopwatch.Elapsed,
                rehearsal);
        }
        finally
        {
            if (!rehearsal)
            {
                _mouseController.EnsureLeftButtonUp();
            }
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
        string message = "",
        bool rehearsal = false)
    {
        var completedClickInPoint = Math.Min(
            checkpoint.ClickIndex,
            point.ClickCount);
        progress?.Report(new ExecutionProgress(
            checkpoint.LoopIndex + 1,
            plan.TotalLoops,
            checkpoint.PointIndex + 1,
            plan.Points.Count,
            rehearsal ? 0 : completedClickInPoint,
            rehearsal ? 0 : point.ClickCount)
        {
            SourceIndex = point.SourceIndex,
            CompletedPointExecutionCount = completedPoints,
            PlannedPointExecutionCount = plan.PlannedPointExecutionCount,
            CompletedClickCount = completedClicks,
            PlannedClickCount = rehearsal ? 0 : plan.PlannedClickCount,
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
        TimeSpan elapsed,
        bool rehearsal = false)
    {
        return new ExecutionResult(
            outcome,
            plan.PlannedPointExecutionCount,
            completedPoints,
            rehearsal ? 0 : plan.PlannedClickCount,
            completedClicks,
            lastSourceIndex,
            checkpoint,
            message,
            elapsed);
    }
}
