using OrderedClicker.Models;

namespace OrderedClicker.Core;

public sealed class LatestExecutionProgress : IProgress<ExecutionProgress>
{
    private ExecutionProgress? _latest;

    public void Report(ExecutionProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Interlocked.Exchange(ref _latest, value);
    }

    public bool TryConsume(out ExecutionProgress? progress)
    {
        progress = Interlocked.Exchange(ref _latest, null);
        return progress is not null;
    }

    public void Clear()
    {
        Interlocked.Exchange(ref _latest, null);
    }
}
