using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Tests;

internal static class CloudDesktopCoordinateServiceTests
{
    public static void Run()
    {
        ConvertsCoordinatesRoundTrip();
        SupportsNegativeScreenCoordinates();
        RejectsSmallRegions();
        RejectsPointsOutsideRegion();
        RecalibrationPreservesExistingRelativeCoordinates();
    }

    private static void RecalibrationPreservesExistingRelativeCoordinates()
    {
        var region = new CloudDesktopRegion
        {
            X = 100,
            Y = 100,
            Width = 1000,
            Height = 600
        };
        var preserved = new ClickPoint
        {
            X = 300,
            Y = 300,
            RelativeX = 0.75,
            RelativeY = 0.25
        };
        var missing = new ClickPoint { X = 600, Y = 400 };

        var filled = CloudDesktopCoordinateService.FillMissingRelativeCoordinates(
            [preserved, missing],
            region);

        TestAssert.Equal(1, filled, "重新校准时只应补充缺失坐标");
        TestAssert.Equal(0.75, preserved.RelativeX!.Value,
            "已有相对横坐标不能被新区域改写");
        TestAssert.Equal(0.25, preserved.RelativeY!.Value,
            "已有相对纵坐标不能被新区域改写");
        TestAssert.Equal(0.5, missing.RelativeX!.Value,
            "缺失坐标应根据新区域补算");
    }

    private static void ConvertsCoordinatesRoundTrip()
    {
        var region = new CloudDesktopRegion
        {
            X = 100,
            Y = 200,
            Width = 1000,
            Height = 500
        };

        var relative = CloudDesktopCoordinateService.ToRelative(region, 600, 450);
        TestAssert.Equal(0.5, relative.X, "X 应换算为区域比例");
        TestAssert.Equal(0.5, relative.Y, "Y 应换算为区域比例");
        TestAssert.Equal(new Point(600, 450),
            CloudDesktopCoordinateService.ToAbsolute(region, relative.X, relative.Y),
            "相对坐标应回放到原位置");
    }

    private static void SupportsNegativeScreenCoordinates()
    {
        var region = new CloudDesktopRegion
        {
            X = -1600,
            Y = 100,
            Width = 1200,
            Height = 800
        };

        var relative = CloudDesktopCoordinateService.ToRelative(region, -1000, 500);

        TestAssert.Equal(0.5, relative.X, "负坐标区域的 X 比例应正确");
        TestAssert.Equal(0.5, relative.Y, "负坐标区域的 Y 比例应正确");
    }

    private static void RejectsSmallRegions()
    {
        var region = new CloudDesktopRegion { Width = 99, Height = 100 };

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => CloudDesktopCoordinateService.ValidateRegion(region),
            "小于 100 像素的云桌面区域应被拒绝");
    }

    private static void RejectsPointsOutsideRegion()
    {
        var region = new CloudDesktopRegion
        {
            X = 100,
            Y = 100,
            Width = 1000,
            Height = 600
        };

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => CloudDesktopCoordinateService.ToRelative(region, 1101, 300),
            "区域外点位应被拒绝");
    }
}
