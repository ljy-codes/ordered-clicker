using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class ProfileServiceTests
{
    public static void Run()
    {
        RoundTripsAllProfileFields();
        ReturnsControlledErrorForMalformedJson();
    }

    private static void RoundTripsAllProfileFields()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var profile = new ClickProfile
        {
            Version = 1,
            Name = "日报录入方案",
            TotalLoops = 12,
            LoopDelayMs = 345,
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
