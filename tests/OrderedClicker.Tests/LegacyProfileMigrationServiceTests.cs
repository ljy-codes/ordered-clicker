using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class LegacyProfileMigrationServiceTests
{
    public static void Run()
    {
        MigratesLegacyProfileToV4();
        WarnsWhenCloudRelativeCoordinatesAreMissing();
        RejectsInvalidLegacyData();
        RejectsNullCloudPointWithControlledError();
        RejectsUnsupportedInputWithoutChangingSource();
    }

    private static void MigratesLegacyProfileToV4()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "legacy.json");
        File.WriteAllText(source,
            """
            {
              "Version": 3,
              "Name": "旧云方案",
              "TotalLoops": 2,
              "LoopDelayMs": 800,
              "CoordinateMode": 1,
              "CloudDesktopRegion": {
                "X": 10, "Y": 20, "Width": 1000, "Height": 700
              },
              "Points": [{
                "X": 110, "Y": 90,
                "RelativeX": 0.1, "RelativeY": 0.1,
                "ClickCount": 2, "ClickIntervalMs": 120, "AfterDelayMs": 300
              }]
            }
            """);
        var original = File.ReadAllText(source);

        var result = new LegacyProfileMigrationService().Migrate(source);

        TestAssert.True(result.Success, result.Error ?? "旧方案应迁移成功");
        TestAssert.Equal(4, result.Profile!.FormatVersion, "迁移结果应为 v4");
        TestAssert.True(result.Profile.ProfileId != Guid.Empty, "迁移应生成新身份");
        TestAssert.Equal("旧云方案", result.Profile.Name, "迁移应保留方案名称");
        TestAssert.Equal(original, File.ReadAllText(source), "迁移不能修改来源文件");
    }

    private static void WarnsWhenCloudRelativeCoordinatesAreMissing()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "missing-relative.json");
        File.WriteAllText(source,
            """
            {
              "Version": 2,
              "Name": "缺少相对坐标",
              "CoordinateMode": 1,
              "CloudDesktopRegion": {
                "X": 0, "Y": 0, "Width": 1000, "Height": 800
              },
              "Points": [{ "X": 500, "Y": 400 }]
            }
            """);

        var result = new LegacyProfileMigrationService().Migrate(source);

        TestAssert.True(result.Success, result.Error ?? "可推导的相对坐标应允许迁移");
        TestAssert.True(result.Warnings.Count > 0, "补算相对坐标时应给出迁移警告");
        TestAssert.Equal(0.5, result.Profile!.Points[0].RelativeX!.Value,
            "应根据旧区域补算相对横坐标");
    }

    private static void RejectsUnsupportedInputWithoutChangingSource()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "future.json");
        File.WriteAllText(source, """{"Version":9,"Name":"未来"}""");
        var original = File.ReadAllText(source);

        var result = new LegacyProfileMigrationService().Migrate(source);

        TestAssert.True(!result.Success, "不支持的旧格式版本应被拒绝");
        TestAssert.Equal(original, File.ReadAllText(source), "失败迁移不能修改来源文件");
    }

    private static void RejectsInvalidLegacyData()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "invalid.json");
        File.WriteAllText(
            source,
            """
            {
              "Version": 3,
              "Name": "非法旧方案",
              "Points": [{
                "X": 10,
                "Y": 20,
                "ClickCount": 0,
                "ClickIntervalMs": 100,
                "AfterDelayMs": 0
              }]
            }
            """);

        var result = new LegacyProfileMigrationService().Migrate(source);

        TestAssert.True(!result.Success, "非法旧方案不应生成无法重新打开的 v4 文件");
        TestAssert.True(
            (result.Error ?? string.Empty).Contains("点击次数", StringComparison.Ordinal),
            "迁移错误应指出具体非法字段");
    }

    private static void RejectsNullCloudPointWithControlledError()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "null-cloud-point.json");
        File.WriteAllText(
            source,
            """
            {
              "Version": 3,
              "Name": "空云点位",
              "CoordinateMode": 1,
              "CloudDesktopRegion": {
                "X": 0, "Y": 0, "Width": 1000, "Height": 800
              },
              "Points": [null]
            }
            """);

        var result = new LegacyProfileMigrationService().Migrate(source);

        TestAssert.True(!result.Success, "空云点位应返回受控迁移失败");
        TestAssert.True(
            (result.Error ?? string.Empty).Contains("点位 1", StringComparison.Ordinal),
            "迁移错误应指出空点位所在序号");
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
