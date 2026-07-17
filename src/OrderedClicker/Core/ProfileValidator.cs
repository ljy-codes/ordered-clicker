using OrderedClicker.Models;

namespace OrderedClicker.Core;

public static class ProfileValidator
{
    public static IReadOnlyList<string> Validate(ClickProfile profile, ScreenBounds virtualScreen)
    {
        var errors = new List<string>();

        if (profile.TotalLoops is < 1 or > 100000)
        {
            errors.Add("总循环次数必须在 1 到 100000 之间。");
        }

        if (profile.LoopDelayMs is < 0 or > 600000)
        {
            errors.Add("轮间等待必须在 0 到 600000 毫秒之间。");
        }

        if (!profile.Points.Any(point => point.Enabled))
        {
            errors.Add("至少需要一个启用的点位。");
        }

        for (var index = 0; index < profile.Points.Count; index++)
        {
            var point = profile.Points[index];
            if (!point.Enabled)
            {
                continue;
            }

            var displayIndex = index + 1;
            if (point.ClickCount is < 1 or > 100000)
            {
                errors.Add($"点位 {displayIndex} 的点击次数必须在 1 到 100000 之间。");
            }

            if (point.ClickIntervalMs is < 10 or > 600000)
            {
                errors.Add($"点位 {displayIndex} 的点击间隔必须在 10 到 600000 毫秒之间。");
            }

            if (point.AfterDelayMs is < 0 or > 600000)
            {
                errors.Add($"点位 {displayIndex} 的点后等待必须在 0 到 600000 毫秒之间。");
            }

            if (!virtualScreen.Contains(point.X, point.Y))
            {
                errors.Add($"点位 {displayIndex} 不在当前虚拟桌面范围内。");
            }
        }

        return errors;
    }
}
