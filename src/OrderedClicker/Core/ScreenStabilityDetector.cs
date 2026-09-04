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
        return await WaitForStableAsync(
            region,
            settings,
            new AsyncPauseGate(),
            cancellationToken);
    }

    public async Task<ScreenStabilityResult> WaitForStableAsync(
        ScreenBounds region,
        ScreenStabilitySettings settings,
        AsyncPauseGate pauseGate,
        CancellationToken cancellationToken,
        IProgress<ScreenStabilityProgress>? progress = null)
    {
        ValidateSettings(settings);
        var pausableDelay = new PausableDelay(_delay);
        await pauseGate.WaitIfPausedAsync(cancellationToken);
        var previous = await _sampler.SampleAsync(region, cancellationToken);
        var elapsedMs = 0;
        var stableMs = 0;

        while (elapsedMs < settings.TimeoutMs)
        {
            await pausableDelay.WaitAsync(
                settings.SampleIntervalMs,
                pauseGate,
                cancellationToken);
            elapsedMs += settings.SampleIntervalMs;

            await pauseGate.WaitIfPausedAsync(cancellationToken);
            var current = await _sampler.SampleAsync(region, cancellationToken);
            var difference = CalculateDifference(previous, current);
            if (difference <= settings.DifferenceTolerance)
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

            progress?.Report(new ScreenStabilityProgress(
                elapsedMs,
                stableMs,
                difference));
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
