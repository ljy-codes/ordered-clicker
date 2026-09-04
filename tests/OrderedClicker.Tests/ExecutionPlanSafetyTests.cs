using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Tests;

internal static class ExecutionPlanSafetyTests
{
    public static void Run()
    {
        FingerprintChangesOnlyForExecutionData();
        EstimatesBaseAndMaximumDuration();
        SafetyCornerRequiresContinuousDwell();
        SafetyCornerContainsOnlyConfiguredCorner();
    }

    private static void SafetyCornerRequiresContinuousDwell()
    {
        var service = new SafetyCornerService();
        var bounds = new ScreenBounds(0, 0, 1920, 1080);
        var start = DateTimeOffset.Parse("2026-09-04T10:00:00+08:00");

        TestAssert.True(!service.Update(
                new Point(3, 3),
                bounds,
                SafetyCorner.TopLeft,
                8,
                TimeSpan.FromMilliseconds(300),
                start),
            "首次进入安全角不应立刻停止");
        TestAssert.True(!service.Update(
                new Point(30, 30),
                bounds,
                SafetyCorner.TopLeft,
                8,
                TimeSpan.FromMilliseconds(300),
                start.AddMilliseconds(200)),
            "离开安全角应重置停留计时");
        TestAssert.True(!service.Update(
                new Point(2, 2),
                bounds,
                SafetyCorner.TopLeft,
                8,
                TimeSpan.FromMilliseconds(300),
                start.AddMilliseconds(300)),
            "重新进入后应重新计时");
        TestAssert.True(service.Update(
                new Point(2, 2),
                bounds,
                SafetyCorner.TopLeft,
                8,
                TimeSpan.FromMilliseconds(300),
                start.AddMilliseconds(601)),
            "连续停留达到阈值后应触发停止");
    }

    private static void SafetyCornerContainsOnlyConfiguredCorner()
    {
        var bounds = new ScreenBounds(-100, -50, 300, 200);

        TestAssert.True(
            SafetyCornerService.Contains(
                new Point(-99, -49),
                bounds,
                SafetyCorner.TopLeft,
                8),
            "左上安全角应支持带负坐标的虚拟桌面");
        TestAssert.True(
            !SafetyCornerService.Contains(
                new Point(191, -49),
                bounds,
                SafetyCorner.TopLeft,
                8),
            "未配置的右上角不应命中");
        TestAssert.True(
            SafetyCornerService.Contains(
                new Point(199, 149),
                bounds,
                SafetyCorner.BottomRight,
                8),
            "右下安全角边界内应命中");
        TestAssert.True(
            !SafetyCornerService.Contains(
                new Point(200, 150),
                bounds,
                SafetyCorner.BottomRight,
                8),
            "虚拟桌面右下边界之外不应命中");
    }

    private static void FingerprintChangesOnlyForExecutionData()
    {
        var profile = CreateProfile();
        var original = ExecutionPlanFingerprintService.Create(profile);
        profile.Name = "仅改显示名称";

        TestAssert.Equal(original, ExecutionPlanFingerprintService.Create(profile),
            "方案名称变化不应让已确认执行计划失效");

        profile.Points[0].AfterDelayMs++;

        TestAssert.True(
            original != ExecutionPlanFingerprintService.Create(profile),
            "执行参数变化必须生成不同指纹");
    }

    private static void EstimatesBaseAndMaximumDuration()
    {
        var profile = CreateProfile();
        profile.TotalLoops = 2;
        profile.LoopDelayMs = 100;
        profile.ScreenStability = new ScreenStabilitySettings
        {
            Enabled = true,
            StableDurationMs = 200,
            TimeoutMs = 1000,
            SampleIntervalMs = 50,
            DifferenceTolerance = 0.02
        };

        var estimate = ExecutionDurationEstimator.Estimate(profile);

        TestAssert.Equal(TimeSpan.FromMilliseconds(1200), estimate.Base,
            "基础耗时应包含点击间隔、点后等待、稳定时长和轮间等待");
        TestAssert.Equal(TimeSpan.FromMilliseconds(2800), estimate.Maximum,
            "最大耗时应使用稳定检测超时时间");
    }

    private static ClickProfile CreateProfile()
    {
        return new ClickProfile
        {
            TotalLoops = 1,
            LoopDelayMs = 0,
            Points =
            [
                new ClickPoint
                {
                    X = 10,
                    Y = 20,
                    ClickCount = 2,
                    ClickIntervalMs = 100,
                    AfterDelayMs = 250
                }
            ]
        };
    }
}
