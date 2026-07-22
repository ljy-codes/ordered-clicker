using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Tests;

internal static class PointTimingServiceTests
{
    public static void Run()
    {
        AppliesClickIntervalWithoutChangingAfterDelay();
        AppliesAfterDelayWithoutChangingClickInterval();
        AppliesDefaultsToNewPoint();
        ResolvesLegacyDefaultsFromFirstPoint();
        ResolvesEmptyLegacyDefaultsFromModelDefaults();
    }

    private static void AppliesClickIntervalWithoutChangingAfterDelay()
    {
        var points = new[]
        {
            new ClickPoint { ClickIntervalMs = 100, AfterDelayMs = 500 },
            new ClickPoint { ClickIntervalMs = 200, AfterDelayMs = 800 }
        };

        PointTimingService.ApplyClickInterval(points, 1000);

        TestAssert.True(
            points.All(point => point.ClickIntervalMs == 1000),
            "全局点击间隔应更新全部点位");
        TestAssert.Equal(500, points[0].AfterDelayMs, "点击间隔批量设置不应修改第一个点的点后等待");
        TestAssert.Equal(800, points[1].AfterDelayMs, "点击间隔批量设置不应修改第二个点的点后等待");
    }

    private static void AppliesAfterDelayWithoutChangingClickInterval()
    {
        var points = new[]
        {
            new ClickPoint { ClickIntervalMs = 100, AfterDelayMs = 500 },
            new ClickPoint { ClickIntervalMs = 200, AfterDelayMs = 800 }
        };

        PointTimingService.ApplyAfterDelay(points, 750);

        TestAssert.True(
            points.All(point => point.AfterDelayMs == 750),
            "全局点后等待应更新全部点位");
        TestAssert.Equal(100, points[0].ClickIntervalMs, "点后等待批量设置不应修改第一个点的点击间隔");
        TestAssert.Equal(200, points[1].ClickIntervalMs, "点后等待批量设置不应修改第二个点的点击间隔");
    }

    private static void AppliesDefaultsToNewPoint()
    {
        var point = new ClickPoint
        {
            ClickIntervalMs = 100,
            AfterDelayMs = 500
        };

        PointTimingService.ApplyDefaults(point, 1400, 900);

        TestAssert.Equal(1400, point.ClickIntervalMs, "新点应继承当前全局点击间隔");
        TestAssert.Equal(900, point.AfterDelayMs, "新点应继承当前全局点后等待");
    }

    private static void ResolvesLegacyDefaultsFromFirstPoint()
    {
        var profile = new ClickProfile
        {
            Version = 1,
            Points =
            [
                new ClickPoint
                {
                    ClickIntervalMs = 1234,
                    AfterDelayMs = 5678
                },
                new ClickPoint
                {
                    ClickIntervalMs = 200,
                    AfterDelayMs = 300
                }
            ]
        };

        var defaults = PointTimingService.ResolveDefaults(profile);

        TestAssert.Equal(1234, defaults.ClickIntervalMs, "旧方案应从第一个点推断全局点击间隔");
        TestAssert.Equal(5678, defaults.AfterDelayMs, "旧方案应从第一个点推断全局点后等待");
    }

    private static void ResolvesEmptyLegacyDefaultsFromModelDefaults()
    {
        var defaults = PointTimingService.ResolveDefaults(new ClickProfile { Version = 1 });

        TestAssert.Equal(100, defaults.ClickIntervalMs, "无点位旧方案应使用默认点击间隔");
        TestAssert.Equal(500, defaults.AfterDelayMs, "无点位旧方案应使用默认点后等待");
    }
}
