namespace OrderedClicker.Models;

public sealed record ExecutionCheckpoint(
    int LoopIndex,
    int PointIndex,
    int ClickIndex,
    long CompletedPointExecutionCount,
    long CompletedClickCount)
{
    public static ExecutionCheckpoint Start { get; } = new(0, 0, 0, 0, 0);
}
