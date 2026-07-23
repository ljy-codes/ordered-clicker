using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Core;

public sealed class ScreenStabilityDetector
{
    private readonly IScreenSampler _sampler;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ScreenStabilityDetector(
        IScreenSampler sampler,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _sampler = sampler;
        _delay = delay ?? Task.Delay;
    }

    public async Task<ScreenStabilityResult> WaitForStableAsync(
        ScreenBounds region,
        ScreenStabilitySettings settings,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var previous = await _sampler.SampleAsync(region, cancellationToken);
        var elapsedMs = 0;
        var stableMs = 0;

        while (elapsedMs < settings.TimeoutMs)
        {
            await _delay(
                TimeSpan.FromMilliseconds(settings.SampleIntervalMs),
                cancellationToken);
            elapsedMs += settings.SampleIntervalMs;

            var current = await _sampler.SampleAsync(region, cancellationToken);
            if (CalculateDifference(previous, current)
                <= settings.DifferenceTolerance)
            {
                stableMs += settings.SampleIntervalMs;
                if (stableMs >= settings.StableDurationMs)
                {
                    return ScreenStabilityResult.Stable;
                }
            }
            else
            {
                stableMs = 0;
            }

            previous = current;
        }

        return ScreenStabilityResult.TimedOut;
    }

    private static double CalculateDifference(byte[] left, byte[] right)
    {
        if (left.Length == 0 || left.Length != right.Length)
        {
            return 1;
        }

        long difference = 0;
        for (var index = 0; index < left.Length; index++)
        {
            difference += Math.Abs(left[index] - right[index]);
        }

        return difference / (left.Length * 255d);
    }

    private static void ValidateSettings(ScreenStabilitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SampleIntervalMs <= 0
            || settings.StableDurationMs <= 0
            || settings.TimeoutMs <= 0
            || settings.DifferenceTolerance is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "画面稳定检测参数无效。");
        }
    }
}
