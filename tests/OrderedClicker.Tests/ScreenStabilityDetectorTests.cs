using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class ScreenStabilityDetectorTests
{
    public static async Task RunAsync()
    {
        await ReturnsStableAfterContinuousMatchingFrames();
        await TimesOutWhileFramesKeepChanging();
        await HonorsCancellation();
    }

    private static async Task ReturnsStableAfterContinuousMatchingFrames()
    {
        var sampler = new SequenceScreenSampler(
            [0, 0, 0],
            [100, 100, 100],
            [100, 100, 100],
            [100, 100, 100]);
        var detector = new ScreenStabilityDetector(
            sampler,
            (_, _) => Task.CompletedTask);

        var result = await detector.WaitForStableAsync(
            new ScreenBounds(0, 0, 500, 300),
            CreateSettings(stableDurationMs: 2, timeoutMs: 10),
            CancellationToken.None);

        TestAssert.Equal(ScreenStabilityResult.Stable, result,
            "连续稳定达到阈值后应继续");
    }

    private static async Task TimesOutWhileFramesKeepChanging()
    {
        var sampler = new SequenceScreenSampler(
            [0], [50], [100], [150], [200], [250]);
        var detector = new ScreenStabilityDetector(
            sampler,
            (_, _) => Task.CompletedTask);

        var result = await detector.WaitForStableAsync(
            new ScreenBounds(0, 0, 500, 300),
            CreateSettings(stableDurationMs: 3, timeoutMs: 4),
            CancellationToken.None);

        TestAssert.Equal(ScreenStabilityResult.TimedOut, result,
            "持续变化超过超时时间应返回超时");
    }

    private static async Task HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var detector = new ScreenStabilityDetector(
            new SequenceScreenSampler([0]),
            (_, token) => Task.FromCanceled(token));

        try
        {
            await detector.WaitForStableAsync(
                new ScreenBounds(0, 0, 500, 300),
                CreateSettings(3, 10),
                cancellation.Token);
            throw new InvalidOperationException("取消时应抛出 OperationCanceledException");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static ScreenStabilitySettings CreateSettings(
        int stableDurationMs,
        int timeoutMs)
    {
        return new ScreenStabilitySettings
        {
            Enabled = true,
            SampleIntervalMs = 1,
            StableDurationMs = stableDurationMs,
            TimeoutMs = timeoutMs,
            DifferenceTolerance = 0.02
        };
    }

    private sealed class SequenceScreenSampler(params byte[][] frames) : IScreenSampler
    {
        private int _index;

        public Task<byte[]> SampleAsync(
            ScreenBounds region,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = frames[Math.Min(_index, frames.Length - 1)];
            _index++;
            return Task.FromResult(frame);
        }
    }
}
