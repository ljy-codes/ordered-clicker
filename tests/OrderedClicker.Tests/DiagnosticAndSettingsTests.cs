using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class DiagnosticAndSettingsTests
{
    public static void Run()
    {
        QuarantinesBrokenSettings();
        WritesActionableDiagnosticLog();
    }

    private static void QuarantinesBrokenSettings()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(settingsPath, "{broken");
        var service = new SettingsService(settingsPath);

        var result = service.LoadWithResult();

        TestAssert.True(result.Warning is not null, "损坏设置应返回可见警告");
        TestAssert.True(!File.Exists(settingsPath), "损坏设置应移出正式路径");
        TestAssert.Equal(1,
            Directory.EnumerateFiles(directory.Path, "settings.*.broken.json").Count(),
            "损坏设置应保留一份隔离副本");
    }

    private static void WritesActionableDiagnosticLog()
    {
        using var directory = new TemporaryDirectory();
        var service = new DiagnosticLogService(directory.Path, "2.0.0-test");

        var path = service.Write(
            "profile.save",
            new IOException("disk full"),
            new Dictionary<string, object?> { ["profileId"] = "abc" });
        var content = File.ReadAllText(path);

        TestAssert.True(content.Contains("profile.save", StringComparison.Ordinal),
            "诊断日志应包含操作名称");
        TestAssert.True(content.Contains("2.0.0-test", StringComparison.Ordinal),
            "诊断日志应包含应用版本");
        TestAssert.True(content.Contains(nameof(IOException), StringComparison.Ordinal),
            "诊断日志应包含异常类型");
        TestAssert.True(content.Contains("disk full", StringComparison.Ordinal),
            "诊断日志应包含异常消息");
        TestAssert.True(content.Contains("profileId", StringComparison.Ordinal),
            "诊断日志应包含上下文");
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
