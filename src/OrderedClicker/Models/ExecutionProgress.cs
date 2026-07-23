namespace OrderedClicker.Models;

public sealed record ExecutionProgress(
    int CurrentLoop,
    int TotalLoops,
    int PointIndex,
    int PointCount,
    int ClickIndex,
    int ClickCount)
{
    public int SourceIndex { get; init; }

    public long CompletedPointExecutionCount { get; init; }

    public long PlannedPointExecutionCount { get; init; }

    public long CompletedClickCount { get; init; }

    public long PlannedClickCount { get; init; }

    public bool RequiresUserContinue { get; init; }

    public string Message { get; init; } = string.Empty;
}
