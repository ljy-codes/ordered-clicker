namespace OrderedClicker.Models;

public enum ExecutionStage
{
    Move,
    Click,
    ClickInterval,
    AfterPointDelay,
    StabilityCheck,
    LoopDelay,
    Completed
}
