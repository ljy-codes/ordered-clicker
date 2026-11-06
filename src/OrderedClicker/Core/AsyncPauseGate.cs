using System.Diagnostics;

namespace OrderedClicker.Core;

public sealed class AsyncPauseGate
{
    private readonly object _sync = new();
    private TaskCompletionSource _resumeSignal = CreateCompletedSignal();
    private bool _isPaused;
    private long _resumeGeneration;
    private long _pauseStarted;
    private TimeSpan _pausedDuration;

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _isPaused;
            }
        }
    }

    public long ResumeGeneration
    {
        get
        {
            lock (_sync)
            {
                return _resumeGeneration;
            }
        }
    }

    internal TimeSpan PausedDuration
    {
        get
        {
            lock (_sync)
            {
                return _pausedDuration + (_isPaused
                    ? Stopwatch.GetElapsedTime(_pauseStarted)
                    : TimeSpan.Zero);
            }
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_isPaused)
            {
                return;
            }

            _isPaused = true;
            _pauseStarted = Stopwatch.GetTimestamp();
            _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource signal;
        lock (_sync)
        {
            if (!_isPaused)
            {
                return;
            }

            _pausedDuration += Stopwatch.GetElapsedTime(_pauseStarted);
            _isPaused = false;
            _resumeGeneration++;
            signal = _resumeSignal;
        }

        signal.TrySetResult();
    }

    public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task waitTask;
            lock (_sync)
            {
                if (!_isPaused)
                {
                    return;
                }

                waitTask = _resumeSignal.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }
}
