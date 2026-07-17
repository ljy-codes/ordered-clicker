namespace OrderedClicker.Models;

public sealed class ClickProfile
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = "默认方案";

    public int TotalLoops { get; set; } = 1;

    public int LoopDelayMs { get; set; } = 1000;

    public List<ClickPoint> Points { get; set; } = [];
}
