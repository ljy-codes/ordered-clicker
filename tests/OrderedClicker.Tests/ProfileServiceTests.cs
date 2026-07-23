using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class ProfileServiceTests
{
    public static void Run()
    {
        RoundTripsAllProfileFields();
        SavesToExplicitPath();
        ExportsAndImportsCompleteProfile();
        RejectsUnsupportedFutureVersion();
        RejectsInvalidPointTiming();
        RejectsInvalidCloudRelativeCoordinate();
        ReturnsControlledErrorForMalformedJson();
    }

    private static void ExportsAndImportsCompleteProfile()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var path = System.IO.Path.Combine(directory.Path, "share", "方案.json");
        var source = CreateCompleteProfile();

        var exportedPath = service.Export(source, path);
        var imported = service.Import(exportedPath);

        TestAssert.Equal(path, exportedPath, "导出应返回用户选择的路径");
        TestAssert.True(imported.Success, imported.ErrorMessage ?? "导入应成功");
        TestAssert.Equal(source.Name, imported.Profile!.Name, "导入应保留方案名称");
        TestAssert.Equal(source.Points.Count, imported.Profile.Points.Count, "导入应保留全部点位");
        TestAssert.Equal(source.CoordinateMode, imported.Profile.CoordinateMode, "导入应保留坐标模式");
        TestAssert.Equal(
            source.ScreenStability.TimeoutMs,
            imported.Profile.ScreenStability.TimeoutMs,
            "导入应保留画面稳定等待设置");
    }

    private static void RejectsUnsupportedFutureVersion()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var path = System.IO.Path.Combine(directory.Path, "future.json");
        File.WriteAllText(
            path,
            """{"Version":99,"Name":"未来方案","TotalLoops":1,"Points":[]}""",
            System.Text.Encoding.UTF8);

        var result = service.Import(path);

        TestAssert.True(!result.Success, "未来版本应被拒绝");
        TestAssert.True(
            (result.ErrorMessage ?? string.Empty).Contains("版本", StringComparison.Ordinal),
            "错误应说明版本不受支持");
    }

    private static void RejectsInvalidPointTiming()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var profile = CreateCompleteProfile();
        profile.CoordinateMode = CoordinateMode.AbsoluteScreen;
        profile.CloudDesktopRegion = null;
        profile.Points[0].ClickCount = 0;
        var path = service.Save(profile, System.IO.Path.Combine(directory.Path, "invalid-point.json"));

        var result = service.Import(path);

        TestAssert.True(!result.Success, "非法点击次数应被拒绝");
        TestAssert.True(
            (result.ErrorMessage ?? string.Empty).Contains("点击次数", StringComparison.Ordinal),
            "错误应指出非法点击次数");
    }

    private static void RejectsInvalidCloudRelativeCoordinate()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var profile = CreateCompleteProfile();
        profile.Points[0].RelativeX = 1.2;
        var path = service.Save(profile, System.IO.Path.Combine(directory.Path, "invalid-cloud.json"));

        var result = service.Import(path);

        TestAssert.True(!result.Success, "非法云桌面相对坐标应被拒绝");
        TestAssert.True(
            (result.ErrorMessage ?? string.Empty).Contains("相对坐标", StringComparison.Ordinal),
            "错误应指出非法云桌面相对坐标");
    }

    private static void SavesToExplicitPath()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var path = System.IO.Path.Combine(directory.Path, "自定义位置", "方案.json");

        var savedPath = service.Save(
            new ClickProfile { Name = "另存为方案" },
            path);

        TestAssert.Equal(path, savedPath, "另存为应返回用户选择的路径");
        TestAssert.True(File.Exists(path), "另存为文件应存在");
    }

    private static void RoundTripsAllProfileFields()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var profile = new ClickProfile
        {
            Version = 2,
            Name = "日报录入方案",
            TotalLoops = 12,
            LoopDelayMs = 345,
            DefaultClickIntervalMs = 1234,
            DefaultAfterDelayMs = 5678,
            Points =
            [
                new ClickPoint
                {
                    Id = Guid.Parse("9c7b1379-d293-4e82-8c50-030ea289934f"),
                    Enabled = true,
                    X = -321,
                    Y = 654,
                    ClickCount = 7,
                    ClickIntervalMs = 88,
                    AfterDelayMs = 999,
                    MonitorDeviceName = "\\\\.\\DISPLAY2",
                    MonitorBounds = new ScreenBounds(-1920, 0, 1920, 1080),
                    CapturedDpi = 144
                }
            ]
        };

        var savedPath = service.Save(profile);
        var result = service.Load(savedPath);

        TestAssert.True(result.Success, $"配置应成功加载：{result.ErrorMessage}");
        TestAssert.Equal("日报录入方案", result.Profile!.Name, "中文方案名应保持不变");
        TestAssert.Equal(12, result.Profile.TotalLoops, "总循环次数应保持不变");
        TestAssert.Equal(1234, result.Profile.DefaultClickIntervalMs, "全局点击间隔应保持不变");
        TestAssert.Equal(5678, result.Profile.DefaultAfterDelayMs, "全局点后等待应保持不变");
        TestAssert.Equal(-321, result.Profile.Points[0].X, "负坐标应保持不变");
        TestAssert.Equal((uint)144, result.Profile.Points[0].CapturedDpi, "DPI 应保持不变");
        TestAssert.True(File.Exists(savedPath), "保存后配置文件应存在");
    }

    private static void ReturnsControlledErrorForMalformedJson()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var path = System.IO.Path.Combine(directory.Path, "损坏.json");
        File.WriteAllText(path, "{not-json", System.Text.Encoding.UTF8);

        var result = service.Load(path);

        TestAssert.True(!result.Success, "损坏 JSON 应返回失败结果");
        TestAssert.True(result.Profile is null, "损坏 JSON 不应返回配置对象");
        TestAssert.True(!string.IsNullOrWhiteSpace(result.ErrorMessage), "失败结果应包含错误信息");
    }

    private static ClickProfile CreateCompleteProfile()
    {
        return new ClickProfile
        {
            Version = 3,
            Name = "云桌面日报方案",
            TotalLoops = 8,
            LoopDelayMs = 1200,
            DefaultClickIntervalMs = 150,
            DefaultAfterDelayMs = 700,
            CoordinateMode = CoordinateMode.CloudDesktopRegion,
            CloudDesktopRegion = new CloudDesktopRegion
            {
                X = 100,
                Y = 80,
                Width = 1200,
                Height = 800,
                MonitorDeviceName = "\\\\.\\DISPLAY1",
                CapturedDpi = 96
            },
            ScreenStability = new ScreenStabilitySettings
            {
                Enabled = true,
                TimeoutMs = 15000
            },
            Points =
            [
                new ClickPoint
                {
                    Enabled = true,
                    X = 400,
                    Y = 300,
                    RelativeX = 0.25,
                    RelativeY = 0.275,
                    ClickCount = 2,
                    ClickIntervalMs = 150,
                    AfterDelayMs = 700,
                    MonitorDeviceName = "\\\\.\\DISPLAY1",
                    MonitorBounds = new ScreenBounds(0, 0, 1920, 1080),
                    CapturedDpi = 96
                }
            ]
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "test-data",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
