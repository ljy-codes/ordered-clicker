namespace OrderedClicker.Models;

public sealed record ExecutionCheckpointEnvelope(
    int FormatVersion,
    string ApplicationVersion,
    ExecutionPlan Plan,
    string PlanFingerprint,
    ExecutionCheckpoint Checkpoint,
    DateTimeOffset SavedAt);
