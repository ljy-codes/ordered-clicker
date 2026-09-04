using OrderedClicker.Models;

namespace OrderedClicker.Core;

public static class CaptureWorkflowService
{
    public static int FindNearbyPoint(
        IReadOnlyList<ClickPoint> points,
        int x,
        int y,
        int tolerancePixels = 5)
    {
        for (var index = 0; index < points.Count; index++)
        {
            if (Math.Abs(points[index].X - x) <= tolerancePixels
                && Math.Abs(points[index].Y - y) <= tolerancePixels)
            {
                return index;
            }
        }

        return -1;
    }
}
