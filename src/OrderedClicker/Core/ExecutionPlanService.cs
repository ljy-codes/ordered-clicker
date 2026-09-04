using OrderedClicker.Models;

namespace OrderedClicker.Core;

public static class ExecutionPlanService
{
    public static ExecutionPlan Create(
        ClickProfile profile,
        ScreenBounds virtualScreen)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var points = profile.Points
            .Select((point, sourceIndex) => (Point: point, SourceIndex: sourceIndex))
            .Where(item => item.Point.Enabled)
            .Select(item => ResolvePoint(
                item.Point,
                item.SourceIndex,
                profile,
                virtualScreen))
            .ToArray();

        var plannedPointExecutions = checked((long)points.Length * profile.TotalLoops);
        var clicksPerLoop = points.Aggregate(
            0L,
            (total, point) => checked(total + point.ClickCount));
        var duration = ExecutionDurationEstimator.Estimate(profile);

        return new ExecutionPlan
        {
            Points = Array.AsReadOnly(points),
            VirtualScreen = virtualScreen,
            TotalSourceRows = profile.Points.Count,
            DisabledSourceRowNumbers = Array.AsReadOnly(profile.Points
                .Select((point, index) => (point, index))
                .Where(item => !item.point.Enabled)
                .Select(item => item.index + 1)
                .ToArray()),
            TotalLoops = profile.TotalLoops,
            LoopDelayMs = profile.LoopDelayMs,
            PlannedPointExecutionCount = plannedPointExecutions,
            PlannedClickCount = checked(clicksPerLoop * profile.TotalLoops),
            ScreenStability = new ScreenStabilitySettings
            {
                Enabled = profile.ScreenStability.Enabled,
                SampleIntervalMs = profile.ScreenStability.SampleIntervalMs,
                StableDurationMs = profile.ScreenStability.StableDurationMs,
                TimeoutMs = profile.ScreenStability.TimeoutMs,
                DifferenceTolerance = profile.ScreenStability.DifferenceTolerance
            },
            StabilityRegion = profile.CoordinateMode == CoordinateMode.CloudDesktopRegion
                ? profile.CloudDesktopRegion!.Bounds
                : virtualScreen,
            ProfileFingerprint = ExecutionPlanFingerprintService.Create(profile),
            EstimatedBaseDuration = duration.Base,
            EstimatedMaximumDuration = duration.Maximum
        };
    }

    private static ExecutionPlanPoint ResolvePoint(
        ClickPoint point,
        int sourceIndex,
        ClickProfile profile,
        ScreenBounds virtualScreen)
    {
        int x;
        int y;

        if (profile.CoordinateMode == CoordinateMode.CloudDesktopRegion)
        {
            var region = profile.CloudDesktopRegion
                ?? throw new InvalidOperationException("云桌面区域尚未校准。");
            if (point.RelativeX is null || point.RelativeY is null)
            {
                throw new InvalidOperationException(
                    $"点位 {sourceIndex + 1} 缺少云桌面相对坐标。");
            }

            var absolute = CloudDesktopCoordinateService.ToAbsolute(
                region,
                point.RelativeX.Value,
                point.RelativeY.Value);
            x = absolute.X;
            y = absolute.Y;
        }
        else
        {
            x = point.X;
            y = point.Y;
        }

        if (!virtualScreen.Contains(x, y))
        {
            throw new InvalidOperationException(
                $"点位 {sourceIndex + 1} 不在当前虚拟桌面范围内。");
        }

        return new ExecutionPlanPoint(
            sourceIndex,
            x,
            y,
            point.ClickCount,
            point.ClickIntervalMs,
            point.AfterDelayMs);
    }
}
