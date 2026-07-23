using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Tests;

internal static class ExecutionPlanServiceTests
{
    public static void Run()
    {
        KeepsSourceRowIndexesAndCounts();
        ResolvesCloudDesktopCoordinates();
    }

    private static void KeepsSourceRowIndexesAndCounts()
    {
        var disabled = CreatePoint(10, 20, 2);
        disabled.Enabled = false;
        var profile = new ClickProfile
        {
            TotalLoops = 3,
            Points =
            [
                CreatePoint(1, 2, 1),
                disabled,
                CreatePoint(3, 4, 2)
            ]
        };

        var plan = ExecutionPlanService.Create(
            profile,
            new ScreenBounds(0, 0, 1920, 1080));

        TestAssert.Equal(2, plan.Points.Count, "执行计划只能包含启用点位");
        TestAssert.Equal(0, plan.Points[0].SourceIndex, "首点应保留原始行号");
        TestAssert.Equal(2, plan.Points[1].SourceIndex, "跳过禁用点后仍应保留原始行号");
        TestAssert.Equal(6L, plan.PlannedPointExecutionCount, "点次总数应包含循环");
        TestAssert.Equal(9L, plan.PlannedClickCount, "点击总数应包含循环");
        TestAssert.Equal("2", string.Join(",", plan.DisabledSourceRowNumbers),
            "禁用行应按用户可见行号报告");
    }

    private static void ResolvesCloudDesktopCoordinates()
    {
        var profile = new ClickProfile
        {
            CoordinateMode = CoordinateMode.CloudDesktopRegion,
            CloudDesktopRegion = new CloudDesktopRegion
            {
                X = 100,
                Y = 200,
                Width = 1000,
                Height = 600
            },
            Points =
            [
                new ClickPoint
                {
                    X = 0,
                    Y = 0,
                    RelativeX = 0.5,
                    RelativeY = 0.5,
                    ClickCount = 1,
                    ClickIntervalMs = 10,
                    AfterDelayMs = 0
                }
            ]
        };

        var plan = ExecutionPlanService.Create(
            profile,
            new ScreenBounds(0, 0, 1920, 1080));

        TestAssert.Equal(600, plan.Points[0].X, "云桌面 X 应在运行前解析");
        TestAssert.Equal(500, plan.Points[0].Y, "云桌面 Y 应在运行前解析");
    }

    private static ClickPoint CreatePoint(int x, int y, int clickCount)
    {
        return new ClickPoint
        {
            X = x,
            Y = y,
            ClickCount = clickCount,
            ClickIntervalMs = 10,
            AfterDelayMs = 0
        };
    }
}
