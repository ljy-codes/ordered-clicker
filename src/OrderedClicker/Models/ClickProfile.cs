using System.Text.Json.Serialization;

namespace OrderedClicker.Models;

public sealed class ClickProfile
{
    public int FormatVersion { get; set; } = 4;

    public Guid ProfileId { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public int Version
    {
        get => FormatVersion;
        set => FormatVersion = value;
    }

    public string Name { get; set; } = "默认方案";

    public int TotalLoops { get; set; } = 1;

    public int LoopDelayMs { get; set; } = 1000;

    public int DefaultClickIntervalMs { get; set; } = 100;

    public int DefaultAfterDelayMs { get; set; } = 500;

    public CoordinateMode CoordinateMode { get; set; } = CoordinateMode.AbsoluteScreen;

    public CloudDesktopRegion? CloudDesktopRegion { get; set; }

    public ScreenStabilitySettings ScreenStability { get; set; } = new();

    public List<ClickPoint> Points { get; set; } = [];
}
