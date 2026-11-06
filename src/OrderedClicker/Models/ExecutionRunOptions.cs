namespace OrderedClicker.Models;

public sealed class ExecutionRunOptions
{
    public ExecutionRunMode Mode { get; set; } = ExecutionRunMode.Normal;

    public Func<ExecutionPlanPoint, string?>? SafetyCheck { get; set; }
}
