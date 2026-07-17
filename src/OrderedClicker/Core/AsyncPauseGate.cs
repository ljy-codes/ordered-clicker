namespace OrderedClicker.Core;

public sealed class AsyncPauseGate
{
    private readonly object _sync = new();
    private TaskCompletionSource _resumeSignal = CreateCompletedSignal();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        lock (_sync)
        {
            if (IsPaused)
            {
                return;
            }

            IsPaused = true;
            _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource signal;
        lock (_sync)
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            signal = _resumeSignal;
        }

        signal.TrySetResult();
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task waitTask;
        lock (_sync)
        {
            waitTask = _resumeSignal.Task;
        }

        return waitTask.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }
}
