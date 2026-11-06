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
        if(profile.TargetWindow is {} target &&
            (!string.Equals(target.Platform,"macOS",StringComparison.OrdinalIgnoreCase)||
             string.IsNullOrWhiteSpace(target.ApplicationId)||string.IsNullOrWhiteSpace(target.WindowTitle)))
            errors.Add("目标窗口绑定无效或来自其他系统，请重新绑定或清除。");
        if(profile.ScreenStability.Region is {} custom && !IsRegionCovered(custom,displays))
            errors.Add("画面检测区域超出当前显示器或包含显示器之间的空隙，请重新选择。");
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

    public static bool IsRegionCovered(ScreenBounds region,IReadOnlyList<MonitorSnapshot> displays)
    {
        if(region.Width<=0||region.Height<=0||(long)region.X+region.Width>int.MaxValue||
            (long)region.Y+region.Height>int.MaxValue)return false;
        var xs=displays.SelectMany(d=>new[]{d.Bounds.X,d.Bounds.Right})
            .Append(region.X).Append(region.Right).Where(x=>x>=region.X&&x<=region.Right).Distinct().Order().ToArray();
        for(var i=0;i<xs.Length-1;i++)
        {
            var coveredTo=region.Y;
            foreach(var bounds in displays.Where(d=>d.Bounds.X<=xs[i]&&d.Bounds.Right>=xs[i+1])
                .Select(d=>d.Bounds).OrderBy(b=>b.Y))
            {
                if(bounds.Y>coveredTo)break;
                coveredTo=Math.Max(coveredTo,bounds.Bottom);
            }
            if(coveredTo<region.Bottom)return false;
        }
        return xs.Length>=2;
    }
}
