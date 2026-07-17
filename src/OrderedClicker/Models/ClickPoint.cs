namespace OrderedClicker.Models;

public sealed class ClickPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public bool Enabled { get; set; } = true;

    public int X { get; set; }

    public int Y { get; set; }

    public int ClickCount { get; set; } = 1;

    public int ClickIntervalMs { get; set; } = 100;

    public int AfterDelayMs { get; set; } = 500;

    public string MonitorDeviceName { get; set; } = string.Empty;

    public ScreenBounds MonitorBounds { get; set; }

    public uint CapturedDpi { get; set; } = 96;
}
