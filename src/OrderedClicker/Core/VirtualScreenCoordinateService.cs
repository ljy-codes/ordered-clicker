using System.Drawing;
using OrderedClicker.Models;

namespace OrderedClicker.Core;

public static class VirtualScreenCoordinateService
{
    private const int AbsoluteMaximum = 65535;

    public static Point Normalize(int x, int y, ScreenBounds virtualScreen)
    {
        if (virtualScreen.Width <= 1 || virtualScreen.Height <= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(virtualScreen),
                "虚拟桌面尺寸无效。");
        }

        if (!virtualScreen.Contains(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"坐标 ({x}, {y}) 不在虚拟桌面范围内。");
        }

        return new Point(
            NormalizeAxis(x, virtualScreen.X, virtualScreen.Width),
            NormalizeAxis(y, virtualScreen.Y, virtualScreen.Height));
    }

    private static int NormalizeAxis(int value, int origin, int length)
    {
        return (int)Math.Round(
            (double)(value - origin) * AbsoluteMaximum / (length - 1));
    }
}
