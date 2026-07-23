namespace OrderedClicker.Models;

public sealed record ExecutionResult(
    ExecutionOutcome Outcome,
    long PlannedPointExecutionCount,
    long CompletedPointExecutionCount,
    long PlannedClickCount,
    long CompletedClickCount,
    int? LastSourceIndex,
    ExecutionCheckpoint NextCheckpoint,
    string Message,
    TimeSpan Elapsed);
