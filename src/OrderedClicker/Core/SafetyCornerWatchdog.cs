using OrderedClicker.Models;

namespace OrderedClicker.Core;

public sealed class SafetyCornerWatchdog : IAsyncDisposable
{
    private readonly Func<bool> _enabled;
    private readonly Func<Point> _cursorPosition;
    private readonly Func<ScreenBounds> _screenBounds;
    private readonly Func<SafetyCorner> _corner;
    private readonly Func<int> _cornerSize;
    private readonly Func<TimeSpan> _dwell;
    private readonly Action _stopRequested;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _task;

    public SafetyCornerWatchdog(
        Func<bool> enabled,
        Func<Point> cursorPosition,
        Func<ScreenBounds> screenBounds,
        Func<SafetyCorner> corner,
        Func<int> cornerSize,
        Func<TimeSpan> dwell,
        Action stopRequested,
        TimeSpan? pollInterval = null)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _cursorPosition = cursorPosition
                          ?? throw new ArgumentNullException(nameof(cursorPosition));
        _screenBounds = screenBounds
                        ?? throw new ArgumentNullException(nameof(screenBounds));
        _corner = corner ?? throw new ArgumentNullException(nameof(corner));
        _cornerSize = cornerSize ?? throw new ArgumentNullException(nameof(cornerSize));
        _dwell = dwell ?? throw new ArgumentNullException(nameof(dwell));
        _stopRequested = stopRequested
                         ?? throw new ArgumentNullException(nameof(stopRequested));
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
    }

    public void Start()
    {
        _task ??= Task.Run(RunAsync);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_task is not null)
        {
            try
            {
                await _task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        var service = new SafetyCornerService();
        while (!_cancellation.IsCancellationRequested)
        {
            if (_enabled()
                && service.Update(
                    _cursorPosition(),
                    _screenBounds(),
                    _corner(),
                    _cornerSize(),
                    _dwell(),
                    DateTimeOffset.UtcNow))
            {
                _stopRequested();
                return;
            }

            await Task.Delay(_pollInterval, _cancellation.Token);
        }
    }
}
