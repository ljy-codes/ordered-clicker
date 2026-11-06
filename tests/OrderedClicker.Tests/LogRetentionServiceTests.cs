using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class LogRetentionServiceTests
{
    public static void Run()
    {
        RetainsLockedOversizedFileWithoutThrowing();
        CountsLockedFilesTowardFileLimit();
        RemovesExpiredAndOldestExcessJsonFiles();
    }

    private static void RetainsLockedOversizedFileWithoutThrowing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.UtcNow;
        CreateFile(directory.Path, "locked.json", 200, now.AddDays(-40));
        CreateFile(directory.Path, "other.json", 10, now.AddMinutes(-1));
        using var locked = new FileStream(Path.Combine(directory.Path, "locked.json"),
            FileMode.Open, FileAccess.Read, FileShare.Read);

        var removed = new LogRetentionService().Prune(directory.Path,
            new LogRetentionPolicy(TimeSpan.FromDays(30), 10, 100), now);

        TestAssert.Equal(1, removed, "超限时只统计真正删除的文件");
        TestAssert.True(File.Exists(Path.Combine(directory.Path, "locked.json")),
            "不可删除日志应保留，清理应有界返回");
    }

    private static void CountsLockedFilesTowardFileLimit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.UtcNow;
        CreateFile(directory.Path, "locked.json", 10, now.AddMinutes(-3));
        CreateFile(directory.Path, "middle.json", 10, now.AddMinutes(-2));
        CreateFile(directory.Path, "new.json", 10, now.AddMinutes(-1));
        using var locked = new FileStream(Path.Combine(directory.Path, "locked.json"),
            FileMode.Open, FileAccess.Read, FileShare.Read);

        var removed = new LogRetentionService().Prune(directory.Path,
            new LogRetentionPolicy(TimeSpan.FromDays(30), 1, 1000), now);

        TestAssert.Equal(2, removed, "失败删除不能减少实际文件数");
        TestAssert.Equal(1, Directory.EnumerateFiles(directory.Path, "*.json").Count(),
            "数量上限必须包含不可删除文件");
    }

    private static void RemovesExpiredAndOldestExcessJsonFiles()
    {
        using var directory = new TemporaryDirectory();
        var now = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        CreateFile(directory.Path, "expired.json", 10, now.AddDays(-31));
        CreateFile(directory.Path, "old.json", 10, now.AddMinutes(-3));
        CreateFile(directory.Path, "middle.json", 10, now.AddMinutes(-2));
        CreateFile(directory.Path, "new.json", 10, now.AddMinutes(-1));
        CreateFile(directory.Path, "keep.txt", 100, now.AddDays(-100));

        var removed = new LogRetentionService().Prune(
            directory.Path,
            new LogRetentionPolicy(TimeSpan.FromDays(30), 2, 100),
            now);

        TestAssert.Equal(2, removed, "应删除过期文件和超出数量的最旧文件");
        TestAssert.True(!File.Exists(Path.Combine(directory.Path, "expired.json")),
            "过期日志应删除");
        TestAssert.True(!File.Exists(Path.Combine(directory.Path, "old.json")),
            "超出数量时应删除最旧日志");
        TestAssert.True(File.Exists(Path.Combine(directory.Path, "keep.txt")),
            "非 JSON 文件不得删除");
    }

    private static void CreateFile(
        string directory,
        string name,
        int bytes,
        DateTimeOffset updatedAt)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, updatedAt.UtcDateTime);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
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
