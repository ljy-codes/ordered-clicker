namespace OrderedClicker.Models;

public sealed record ScreenStabilityProgress(
    int ElapsedMilliseconds,
    int StableMilliseconds,
    double Difference);
