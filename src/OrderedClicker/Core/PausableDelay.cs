namespace OrderedClicker.Core;

public sealed class PausableDelay
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _sliceMilliseconds;

    public PausableDelay(
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int sliceMilliseconds = 25)
    {
        if (sliceMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sliceMilliseconds));
        }

        _delay = delay ?? Task.Delay;
        _sliceMilliseconds = sliceMilliseconds;
    }

    public async Task WaitAsync(
        int milliseconds,
        AsyncPauseGate pauseGate,
        CancellationToken cancellationToken)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        var remaining = milliseconds;
        while (remaining > 0)
        {
            await pauseGate.WaitIfPausedAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var slice = Math.Min(remaining, _sliceMilliseconds);
            await _delay(TimeSpan.FromMilliseconds(slice), cancellationToken);
            remaining -= slice;
        }
    }
}
