using OrderedClicker.Models;

namespace OrderedClicker.Core;

public static class PointTimingService
{
    public static void ApplyClickInterval(IEnumerable<ClickPoint> points, int value)
    {
        foreach (var point in points)
        {
            point.ClickIntervalMs = value;
        }
    }

    public static void ApplyAfterDelay(IEnumerable<ClickPoint> points, int value)
    {
        foreach (var point in points)
        {
            point.AfterDelayMs = value;
        }
    }

    public static void ApplyDefaults(
        ClickPoint point,
        int clickIntervalMs,
        int afterDelayMs)
    {
        point.ClickIntervalMs = clickIntervalMs;
        point.AfterDelayMs = afterDelayMs;
    }

    public static (int ClickIntervalMs, int AfterDelayMs) ResolveDefaults(ClickProfile profile)
    {
        if (profile.Version >= 2)
        {
            return (profile.DefaultClickIntervalMs, profile.DefaultAfterDelayMs);
        }

        var firstPoint = profile.Points.FirstOrDefault();
        if (firstPoint is not null)
        {
            return (firstPoint.ClickIntervalMs, firstPoint.AfterDelayMs);
        }

        var modelDefaults = new ClickPoint();
        return (modelDefaults.ClickIntervalMs, modelDefaults.AfterDelayMs);
    }
}
