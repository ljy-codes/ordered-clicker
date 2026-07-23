using System.Drawing;
using OrderedClicker.Models;

namespace OrderedClicker.Core;

public static class CloudDesktopCoordinateService
{
    public static (double X, double Y) ToRelative(
        CloudDesktopRegion region,
        int x,
        int y)
    {
        ValidateRegion(region);
        if (!region.Bounds.Contains(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"点位 ({x}, {y}) 不在云桌面区域内。");
        }

        return (
            (double)(x - region.X) / region.Width,
            (double)(y - region.Y) / region.Height);
    }

    public static Point ToAbsolute(
        CloudDesktopRegion region,
        double relativeX,
        double relativeY)
    {
        ValidateRegion(region);
        ValidateRelative(relativeX, relativeY);

        var x = region.X + (int)Math.Round(relativeX * region.Width);
        var y = region.Y + (int)Math.Round(relativeY * region.Height);

        return new Point(
            Math.Clamp(x, region.X, region.X + region.Width - 1),
            Math.Clamp(y, region.Y, region.Y + region.Height - 1));
    }

    public static void ValidateRegion(CloudDesktopRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (region.Width < 100 || region.Height < 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                "云桌面区域宽度和高度都不能小于 100 像素。");
        }
    }

    public static void ValidateRelative(double relativeX, double relativeY)
    {
        if (!double.IsFinite(relativeX)
            || !double.IsFinite(relativeY)
            || relativeX is < 0 or > 1
            || relativeY is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativeX),
                "云桌面相对坐标必须在 0 到 1 之间。");
        }
    }
}
