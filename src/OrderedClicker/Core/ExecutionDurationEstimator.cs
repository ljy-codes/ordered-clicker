using OrderedClicker.Models;

namespace OrderedClicker.Core;

public sealed record ExecutionDurationEstimate(TimeSpan Base, TimeSpan Maximum);

public static class ExecutionDurationEstimator
{
    public static ExecutionDurationEstimate Estimate(ClickProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var enabledPoints = profile.Points.Where(point => point.Enabled).ToArray();
        var perLoopWithoutStability = enabledPoints.Aggregate(
            0L,
            (total, point) => checked(
                total
                + (long)Math.Max(0, point.ClickCount - 1) * point.ClickIntervalMs
                + point.AfterDelayMs));
        var baseStability = profile.ScreenStability.Enabled
            ? checked((long)enabledPoints.Length * profile.ScreenStability.StableDurationMs)
            : 0L;
        var maximumStability = profile.ScreenStability.Enabled
            ? checked((long)enabledPoints.Length * profile.ScreenStability.TimeoutMs)
            : 0L;
        var loopDelay = checked(
            (long)Math.Max(0, profile.TotalLoops - 1) * profile.LoopDelayMs);
        var baseMilliseconds = checked(
            (perLoopWithoutStability + baseStability) * profile.TotalLoops + loopDelay);
        var maximumMilliseconds = checked(
            (perLoopWithoutStability + maximumStability) * profile.TotalLoops + loopDelay);
        return new ExecutionDurationEstimate(
            TimeSpan.FromMilliseconds(baseMilliseconds),
            TimeSpan.FromMilliseconds(maximumMilliseconds));
    }
}
