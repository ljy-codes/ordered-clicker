namespace OrderedClicker.Models;

public sealed record ExecutionPlanPoint(
    int SourceIndex,
    int X,
    int Y,
    int ClickCount,
    int ClickIntervalMs,
    int AfterDelayMs);
