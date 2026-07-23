using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Tests;

internal static class VirtualScreenCoordinateServiceTests
{
    public static void Run()
    {
        NormalizesPrimaryDisplay();
        NormalizesNegativeVirtualDesktopCoordinates();
    }

    private static void NormalizesPrimaryDisplay()
    {
        var normalized = VirtualScreenCoordinateService.Normalize(
            1919,
            1079,
            new ScreenBounds(0, 0, 1920, 1080));

        TestAssert.Equal(65535, normalized.X, "主屏幕最右侧应归一化为最大值");
        TestAssert.Equal(65535, normalized.Y, "主屏幕最下侧应归一化为最大值");
    }

    private static void NormalizesNegativeVirtualDesktopCoordinates()
    {
        var normalized = VirtualScreenCoordinateService.Normalize(
            -960,
            540,
            new ScreenBounds(-1920, 0, 3840, 1080));

        TestAssert.Equal(16388, normalized.X, "负坐标桌面 X 应按虚拟屏幕归一化");
        TestAssert.Equal(32798, normalized.Y, "Y 应按虚拟屏幕归一化");
    }
}
