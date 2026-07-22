namespace OrderedClicker.Models;

public sealed class ClickProfile
{
    public int Version { get; set; } = 2;

    public string Name { get; set; } = "默认方案";

    public int TotalLoops { get; set; } = 1;

    public int LoopDelayMs { get; set; } = 1000;

    public int DefaultClickIntervalMs { get; set; } = 100;

    public int DefaultAfterDelayMs { get; set; } = 500;

    public List<ClickPoint> Points { get; set; } = [];
}
