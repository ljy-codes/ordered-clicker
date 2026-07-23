namespace OrderedClicker.Models;

public sealed class ScreenStabilitySettings
{
    public bool Enabled { get; set; }

    public int SampleIntervalMs { get; set; } = 250;

    public int StableDurationMs { get; set; } = 750;

    public int TimeoutMs { get; set; } = 15000;

    public double DifferenceTolerance { get; set; } = 0.02;
}
