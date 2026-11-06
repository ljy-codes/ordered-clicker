using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Tests;

internal static class ProfileValidatorTests
{
    public static void Run()
    {
        RejectsInvalidLoopCount();
        RejectsInvalidPointSettings();
        AcceptsNegativeCoordinatesInsideVirtualScreen();
        RejectsMissingEnabledPoints();
        RejectsOutOfBoundsPointAndInvalidDelays();
        ValidatesTheEntireCustomStabilityRegion();
        RejectsIncompleteTargetWindowBindings();
    }

    private static void ValidatesTheEntireCustomStabilityRegion()
    {
        var profile = CreateValidProfile();
        var desktop = new ScreenBounds(-100, -50, 300, 200);
        foreach (var region in new[]
                 {
                     new ScreenBounds(0, 0, 0, 10),
                     new ScreenBounds(0, 0, 10, -1),
                     new ScreenBounds(-101, 0, 20, 20),
                     new ScreenBounds(0, -51, 20, 20),
                     new ScreenBounds(190, 0, 11, 20),
                     new ScreenBounds(0, 140, 20, 11),
                     new ScreenBounds(100, 0, int.MaxValue, 20)
                 })
        {
            profile.ScreenStability.Region = region;
            TestAssert.True(ProfileValidator.Validate(profile, desktop).Any(error =>
                    error.Contains("区域", StringComparison.Ordinal)),
                "自定义区域必须尺寸为正且完全在虚拟桌面内，不能溢出绕过校验");
        }

        foreach (var region in new[]
                 {
                     desktop,
                     new ScreenBounds(-100, -50, 1, 1),
                     new ScreenBounds(199, 149, 1, 1)
                 })
        {
            profile.ScreenStability.Region = region;
            TestAssert.Equal(0, ProfileValidator.Validate(profile, desktop).Count,
                "桌面边缘及负坐标内有效区域应通过校验");
        }
    }

    private static void RejectsIncompleteTargetWindowBindings()
    {
        var profile = CreateValidProfile();
        var desktop = new ScreenBounds(0, 0, 200, 200);
        foreach (var binding in new[]
                 {
                     new TargetWindowBinding(),
                     new TargetWindowBinding { Platform = "windows", WindowTitle = "title" },
                     new TargetWindowBinding { Platform = "windows", ApplicationId = "app" }
                 })
        {
            profile.TargetWindow = binding;
            TestAssert.True(ProfileValidator.Validate(profile, desktop).Any(error =>
                    error.Contains("窗口", StringComparison.Ordinal)),
                "不完整窗口绑定必须拒绝");
        }

        profile.TargetWindow = new TargetWindowBinding
        {
            Platform = "macos", ApplicationId = "app", WindowTitle = "title"
        };
        TestAssert.Equal(0, ProfileValidator.Validate(profile, desktop).Count,
            "共享校验应接受完整的平台绑定，当前平台匹配由适配器检查");
        profile.TargetWindow = null;
        TestAssert.Equal(0, ProfileValidator.Validate(profile, desktop).Count, "空绑定应关闭窗口保护");
    }

    private static void RejectsInvalidLoopCount()
    {
        var profile = CreateValidProfile();
        profile.TotalLoops = 0;

        var errors = ProfileValidator.Validate(profile, new ScreenBounds(-1920, 0, 3840, 1080));

        TestAssert.True(errors.Any(error => error.Contains("总循环次数", StringComparison.Ordinal)),
            "总循环次数为 0 时应返回校验错误");
    }

    private static void RejectsInvalidPointSettings()
    {
        var profile = CreateValidProfile();
        profile.Points[0].ClickCount = 0;
        profile.Points[0].ClickIntervalMs = 9;

        var errors = ProfileValidator.Validate(profile, new ScreenBounds(0, 0, 1920, 1080));

        TestAssert.True(errors.Any(error => error.Contains("点击次数", StringComparison.Ordinal)),
            "点击次数为 0 时应返回校验错误");
        TestAssert.True(errors.Any(error => error.Contains("点击间隔", StringComparison.Ordinal)),
            "点击间隔低于 10ms 时应返回校验错误");
    }

    private static void AcceptsNegativeCoordinatesInsideVirtualScreen()
    {
        var profile = CreateValidProfile();
        profile.Points[0].X = -100;
        profile.Points[0].Y = 200;

        var errors = ProfileValidator.Validate(profile, new ScreenBounds(-1920, 0, 3840, 1080));

        TestAssert.True(errors.Count == 0, "虚拟桌面内的负坐标应通过校验");
    }

    private static void RejectsMissingEnabledPoints()
    {
        var profile = CreateValidProfile();
        profile.Points[0].Enabled = false;

        var errors = ProfileValidator.Validate(profile, new ScreenBounds(0, 0, 1920, 1080));

        TestAssert.True(errors.Any(error => error.Contains("启用的点位", StringComparison.Ordinal)),
            "没有启用点位时应拒绝启动");
    }

    private static void RejectsOutOfBoundsPointAndInvalidDelays()
    {
        var profile = CreateValidProfile();
        profile.LoopDelayMs = -1;
        profile.Points[0].X = 1920;
        profile.Points[0].AfterDelayMs = 600001;

        var errors = ProfileValidator.Validate(profile, new ScreenBounds(0, 0, 1920, 1080));

        TestAssert.True(errors.Any(error => error.Contains("轮间等待", StringComparison.Ordinal)),
            "轮间等待为负数时应返回校验错误");
        TestAssert.True(errors.Any(error => error.Contains("点后等待", StringComparison.Ordinal)),
            "点后等待超过上限时应返回校验错误");
        TestAssert.True(errors.Any(error => error.Contains("虚拟桌面", StringComparison.Ordinal)),
            "点位越界时应返回校验错误");
    }

    private static ClickProfile CreateValidProfile()
    {
        return new ClickProfile
        {
            Name = "测试方案",
            TotalLoops = 1,
            LoopDelayMs = 0,
            Points =
            [
                new ClickPoint
                {
                    X = 100,
                    Y = 100,
                    ClickCount = 1,
                    ClickIntervalMs = 100,
                    AfterDelayMs = 0,
                    Enabled = true
                }
            ]
        };
    }
}
