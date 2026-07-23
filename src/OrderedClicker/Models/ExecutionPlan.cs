namespace OrderedClicker.Models;

public sealed class ExecutionPlan
{
    public required IReadOnlyList<ExecutionPlanPoint> Points { get; init; }

    public required ScreenBounds VirtualScreen { get; init; }

    public int TotalSourceRows { get; init; }

    public required IReadOnlyList<int> DisabledSourceRowNumbers { get; init; }

    public int TotalLoops { get; init; }

    public int LoopDelayMs { get; init; }

    public long PlannedPointExecutionCount { get; init; }

    public long PlannedClickCount { get; init; }

    public required ScreenStabilitySettings ScreenStability { get; init; }

    public required ScreenBounds StabilityRegion { get; init; }
}
