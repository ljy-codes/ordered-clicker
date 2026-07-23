using System.Diagnostics;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Core;

public sealed class ClickExecutionEngine
{
    private readonly IMouseController _mouseController;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ScreenStabilityDetector? _stabilityDetector;

    public ClickExecutionEngine(
        IMouseController mouseController,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ScreenStabilityDetector? stabilityDetector = null)
    {
        _mouseController = mouseController;
        _delay = delay ?? Task.Delay;
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
        var stopwatch = Stopwatch.StartNew();
        var completedPoints = checkpoint.CompletedPointExecutionCount;
        var completedClicks = checkpoint.CompletedClickCount;
        var nextCheckpoint = checkpoint;
        int? lastSourceIndex = null;

        try
        {
            ValidateCheckpoint(plan, checkpoint);

            for (var loopIndex = checkpoint.LoopIndex;
                 loopIndex < plan.TotalLoops;
                 loopIndex++)
            {
                var firstPointIndex = loopIndex == checkpoint.LoopIndex
                    ? checkpoint.PointIndex
                    : 0;

                for (var pointIndex = firstPointIndex;
                     pointIndex < plan.Points.Count;
                     pointIndex++)
                {
                    var point = plan.Points[pointIndex];
                    var firstClickIndex =
                        loopIndex == checkpoint.LoopIndex
                        && pointIndex == checkpoint.PointIndex
                            ? checkpoint.ClickIndex
                            : 0;

                    await pauseGate.WaitIfPausedAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    _mouseController.MoveTo(
                        point.X,
                        point.Y,
                        plan.VirtualScreen);

                    for (var clickIndex = firstClickIndex;
                         clickIndex < point.ClickCount;
                         clickIndex++)
                    {
                        await pauseGate.WaitIfPausedAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        _mouseController.LeftClick();
                        completedClicks++;
                        lastSourceIndex = point.SourceIndex;

                        var pointFinished = clickIndex + 1 == point.ClickCount;
                        if (pointFinished)
                        {
                            completedPoints++;
                        }

                        nextCheckpoint = CalculateNextCheckpoint(
                            plan,
                            loopIndex,
                            pointIndex,
                            clickIndex,
                            completedPoints,
                            completedClicks);

                        progress?.Report(new ExecutionProgress(
                            loopIndex + 1,
                            plan.TotalLoops,
                            pointIndex + 1,
                            plan.Points.Count,
                            clickIndex + 1,
                            point.ClickCount)
                        {
                            SourceIndex = point.SourceIndex,
                            CompletedPointExecutionCount = completedPoints,
                            PlannedPointExecutionCount = plan.PlannedPointExecutionCount,
                            CompletedClickCount = completedClicks,
                            PlannedClickCount = plan.PlannedClickCount
                        });

                        if (!pointFinished)
                        {
                            await DelayAsync(point.ClickIntervalMs, cancellationToken);
                        }
                    }

                    await DelayAsync(point.AfterDelayMs, cancellationToken);

                    if (plan.ScreenStability.Enabled
                        && _stabilityDetector is not null)
                    {
                        var stability = await _stabilityDetector.WaitForStableAsync(
                            plan.StabilityRegion,
                            plan.ScreenStability,
                            cancellationToken);
                        if (stability == ScreenStabilityResult.TimedOut)
                        {
                            pauseGate.Pause();
                            progress?.Report(new ExecutionProgress(
                                loopIndex + 1,
                                plan.TotalLoops,
                                pointIndex + 1,
                                plan.Points.Count,
                                point.ClickCount,
                                point.ClickCount)
                            {
                                SourceIndex = point.SourceIndex,
                                CompletedPointExecutionCount = completedPoints,
                                PlannedPointExecutionCount = plan.PlannedPointExecutionCount,
                                CompletedClickCount = completedClicks,
                                PlannedClickCount = plan.PlannedClickCount,
                                RequiresUserContinue = true,
                                Message = $"点位 {point.SourceIndex + 1} 后画面未稳定"
                            });
                            await pauseGate.WaitIfPausedAsync(cancellationToken);
                        }
                    }
                }

                if (loopIndex + 1 < plan.TotalLoops)
                {
                    await DelayAsync(plan.LoopDelayMs, cancellationToken);
                }
            }

            var completed =
                completedPoints == plan.PlannedPointExecutionCount
                && completedClicks == plan.PlannedClickCount;

            return CreateResult(
                completed ? ExecutionOutcome.Completed : ExecutionOutcome.Failed,
                completed
                    ? "全部计划点位和点击次数已完成。"
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

    private static void ValidateCheckpoint(
        ExecutionPlan plan,
        ExecutionCheckpoint checkpoint)
    {
        if (checkpoint.LoopIndex < 0
            || checkpoint.LoopIndex > plan.TotalLoops
            || checkpoint.PointIndex < 0
            || checkpoint.PointIndex > plan.Points.Count
            || checkpoint.ClickIndex < 0)
        {
            throw new InvalidOperationException("执行断点无效。");
        }

        if (checkpoint.LoopIndex < plan.TotalLoops
            && checkpoint.PointIndex < plan.Points.Count
            && checkpoint.ClickIndex >= plan.Points[checkpoint.PointIndex].ClickCount)
        {
            throw new InvalidOperationException("执行断点的点击序号无效。");
        }
    }

    private static ExecutionCheckpoint CalculateNextCheckpoint(
        ExecutionPlan plan,
        int loopIndex,
        int pointIndex,
        int clickIndex,
        long completedPoints,
        long completedClicks)
    {
        if (clickIndex + 1 < plan.Points[pointIndex].ClickCount)
        {
            return new ExecutionCheckpoint(
                loopIndex,
                pointIndex,
                clickIndex + 1,
                completedPoints,
                completedClicks);
        }

        if (pointIndex + 1 < plan.Points.Count)
        {
            return new ExecutionCheckpoint(
                loopIndex,
                pointIndex + 1,
                0,
                completedPoints,
                completedClicks);
        }

        return new ExecutionCheckpoint(
            loopIndex + 1,
            0,
            0,
            completedPoints,
            completedClicks);
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

    private Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        return milliseconds <= 0
            ? Task.CompletedTask
            : _delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
    }
}
