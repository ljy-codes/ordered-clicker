namespace OrderedClicker.Models;

public sealed record ExecutionProgress(
    int CurrentLoop,
    int TotalLoops,
    int PointIndex,
    int PointCount,
    int ClickIndex,
    int ClickCount);
