namespace OrderedClicker.Core;

public sealed class ReplaceableDelay : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    public async Task RunAsync(
        TimeSpan delay,
        Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
        }

        try
        {
            await Task.Delay(delay, cancellation.Token);
            await action(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _cancellation?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }
}
