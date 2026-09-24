using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class LogRetentionServiceTests
{
    public static void Run()
    {
        RemovesExpiredAndOldestExcessJsonFiles();
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
