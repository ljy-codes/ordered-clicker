using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Mac.Platform;

public static class MacPlanValidation
{
    public static IReadOnlyList<string> Validate(ClickProfile profile, AppSettings settings,
        IReadOnlyList<MonitorSnapshot> displays)
    {
        var errors = new List<string>();
        if (displays.Count == 0) return ["没有可用显示器。"];
        var bounds = MacNative.VirtualBounds(displays);
        errors.AddRange(ProfileValidator.Validate(profile, bounds));
        var formatError = ProfileService.ValidateProfile(profile);
        if (formatError != null) errors.Add(formatError);
        if (!settings.SafetyCornerEnabled) errors.Add("运行前必须启用安全角停止。");
        if (profile.CoordinateMode == CoordinateMode.CloudDesktopRegion && profile.CloudDesktopRegion is { } region)
        {
            var monitor = displays.FirstOrDefault(d => d.DeviceName == region.MonitorDeviceName);
            if (monitor == null || monitor.Dpi != region.CapturedDpi ||
                !monitor.Bounds.Contains(region.X, region.Y) || !monitor.Bounds.Contains(region.X+region.Width-1, region.Y+region.Height-1))
                errors.Add("云桌面所属显示器或缩放已变化，请重新校准区域。");
        }
        else if (profile.CoordinateMode == CoordinateMode.AbsoluteScreen)
        {
            foreach (var item in profile.Points.Select((p,i) => (p,i)).Where(v => v.p.Enabled))
            {
                var monitor = displays.FirstOrDefault(d => d.DeviceName == item.p.MonitorDeviceName);
                if (monitor == null || monitor.Bounds != item.p.MonitorBounds || monitor.Dpi != item.p.CapturedDpi)
                    errors.Add($"点位 {item.i+1} 的显示器信息已变化或来自其他系统，请重新采点。");
            }
        }
        if (errors.Count == 0)
        {
            var plan = ExecutionPlanService.Create(profile, bounds);
            foreach (var p in plan.Points)
            {
                if (!displays.Any(d => d.Bounds.Contains(p.X,p.Y)))
                    errors.Add($"点位 {p.SourceIndex+1} 位于显示器之间的空隙，请重新采点。");
                if (displays.Any(d => SafetyCornerService.Contains(new(p.X,p.Y), d.Bounds, settings.SafetyCorner, settings.SafetyCornerSize)))
                    errors.Add($"点位 {p.SourceIndex+1} 与安全角重叠，请调整点位或安全角。");
            }
        }
        return errors.Distinct().ToArray();
    }
}
