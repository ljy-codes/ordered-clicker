using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class DiagnosticAndSettingsTests
{
    public static void Run()
    {
        PreservesTemporarilyUnreadableSettings();
        PreservesBrokenSettingsWhenQuarantineIsLocked();
        QuarantinesBrokenSettings();
        WritesActionableDiagnosticLog();
    }

    private static void PreservesTemporarilyUnreadableSettings()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var service = new SettingsService(path);
        service.Save(new OrderedClicker.Models.AppSettings());
        var original = File.ReadAllText(path);
        SettingsLoadResult result;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                   FileShare.None))
        {
            result = service.LoadWithResult();
            TestAssert.True(result.Warning is not null
                && !result.Warning.Contains("损坏", StringComparison.Ordinal),
                "访问失败应报告读取警告而不是损坏");
            TestAssert.True(result.BrokenSettingsPath is null, "访问失败不得隔离文件");
        }

        TestAssert.Throws<IOException>(() => service.Save(result.Settings),
            "读取失败后的默认设置不得在锁释放后静默覆盖原文件");
        TestAssert.Equal(original, File.ReadAllText(path), "读取失败后原设置必须保持");
        service.LoadWithResult();
        service.Save(result.Settings);
    }

    private static void PreservesBrokenSettingsWhenQuarantineIsLocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{broken");
        var service = new SettingsService(path);
        SettingsLoadResult result;
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = service.LoadWithResult();
        }

        TestAssert.True(result.Warning is not null, "隔离失败仍应返回默认设置和警告");
        TestAssert.True(result.BrokenSettingsPath is null, "隔离失败不得声称存在隔离副本");
        TestAssert.Equal("{broken", File.ReadAllText(path), "隔离失败必须保留原文件");
        TestAssert.Throws<IOException>(() => service.Save(result.Settings),
            "隔离失败后普通保存不得覆盖原文件");
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
