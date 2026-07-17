using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Core;

public sealed class ClickExecutionEngine
{
    private readonly IMouseController _mouseController;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ClickExecutionEngine(
        IMouseController mouseController,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _mouseController = mouseController;
        _delay = delay ?? Task.Delay;
    }

    public async Task ExecuteAsync(
        ClickProfile profile,
        AsyncPauseGate pauseGate,
        IProgress<ExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var points = profile.Points
            .Where(point => point.Enabled)
            .Select(point => new PointSnapshot(
                point.X,
                point.Y,
                point.ClickCount,
                point.ClickIntervalMs,
                point.AfterDelayMs))
            .ToArray();

        try
        {
            for (var loopIndex = 0; loopIndex < profile.TotalLoops; loopIndex++)
            {
                for (var pointIndex = 0; pointIndex < points.Length; pointIndex++)
                {
                    var point = points[pointIndex];
                    await pauseGate.WaitIfPausedAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    _mouseController.MoveTo(point.X, point.Y);

                    for (var clickIndex = 0; clickIndex < point.ClickCount; clickIndex++)
                    {
                        await pauseGate.WaitIfPausedAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        _mouseController.LeftClick();
                        progress?.Report(new ExecutionProgress(
                            loopIndex + 1,
                            profile.TotalLoops,
                            pointIndex + 1,
                            points.Length,
                            clickIndex + 1,
                            point.ClickCount));

                        if (clickIndex + 1 < point.ClickCount)
                        {
                            await DelayAsync(point.ClickIntervalMs, cancellationToken);
                        }
                    }

                    await DelayAsync(point.AfterDelayMs, cancellationToken);
                }

                if (loopIndex + 1 < profile.TotalLoops)
                {
                    await DelayAsync(profile.LoopDelayMs, cancellationToken);
                }
            }
        }
        finally
        {
            _mouseController.EnsureLeftButtonUp();
        }
    }

    private Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        return milliseconds <= 0
            ? Task.CompletedTask
            : _delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
    }

    private sealed record PointSnapshot(
        int X,
        int Y,
        int ClickCount,
        int ClickIntervalMs,
        int AfterDelayMs);
}
