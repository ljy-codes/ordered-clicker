using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderedClicker.Models;

namespace OrderedClicker.Core;

public static class ExecutionPlanFingerprintService
{
    public static string Create(ClickProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var payload = JsonSerializer.Serialize(new
        {
            profile.TotalLoops,
            profile.LoopDelayMs,
            profile.CoordinateMode,
            profile.CloudDesktopRegion,
            Stability = new
            {
                profile.ScreenStability.Enabled,
                profile.ScreenStability.SampleIntervalMs,
                profile.ScreenStability.StableDurationMs,
                profile.ScreenStability.TimeoutMs,
                profile.ScreenStability.DifferenceTolerance
            },
            Points = profile.Points.Select(point => new
            {
                point.Enabled,
                point.X,
                point.Y,
                point.RelativeX,
                point.RelativeY,
                point.ClickCount,
                point.ClickIntervalMs,
                point.AfterDelayMs
            })
        });
        // Keep the original v4 payload byte-for-byte when both optional guards are absent.
        if (profile.TargetWindow is not null || profile.ScreenStability.Region is not null)
        {
            payload += JsonSerializer.Serialize(new
            {
                profile.TargetWindow,
                profile.ScreenStability.Region
            });
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
