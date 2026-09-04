using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class AppDataPathsTests
{
    public static void Run()
    {
        ResolvesInstalledDataRoot();
        ResolvesPortableDataRoot();
        RejectsUnwritablePortableDataRoot();
    }

    private static void ResolvesInstalledDataRoot()
    {
        using var directory = new TemporaryDirectory();

        var paths = AppDataPaths.Resolve(
            @"C:\Program Files\OrderedClicker\OrderedClicker.exe",
            directory.Path,
            false);

        TestAssert.True(!paths.IsPortable, "安装版不应标记为免安装模式");
        TestAssert.Equal(
            Path.Combine(directory.Path, "OrderedClicker"),
            paths.Root,
            "安装版应使用 LocalAppData 下的独立目录");
        AssertDirectories(paths);
    }

    private static void ResolvesPortableDataRoot()
    {
        using var directory = new TemporaryDirectory();
        var executablePath = Path.Combine(directory.Path, "用户重命名.exe");

        var paths = AppDataPaths.Resolve(executablePath, directory.Path, true);

        TestAssert.True(paths.IsPortable, "便携构建改名后仍应保持便携数据模式");
        TestAssert.Equal(
            Path.Combine(directory.Path, "OrderedClickerData"),
            paths.Root,
            "免安装版应把数据放在程序旁边");
        AssertDirectories(paths);
    }

    private static void RejectsUnwritablePortableDataRoot()
    {
        using var directory = new TemporaryDirectory();
        var dataRoot = Path.Combine(directory.Path, "OrderedClickerData");
        File.WriteAllText(dataRoot, "occupied");

        TestAssert.Throws<IOException>(
            () => AppDataPaths.Resolve(
                Path.Combine(directory.Path, "有序连点器-免安装.exe"),
                directory.Path,
                true),
            "免安装数据目录不可创建时应明确失败");
    }

    private static void AssertDirectories(AppDataPaths paths)
    {
        TestAssert.True(Directory.Exists(paths.Profiles), "方案目录应自动创建");
        TestAssert.True(Directory.Exists(paths.Drafts), "草稿目录应自动创建");
        TestAssert.True(Directory.Exists(paths.Logs), "运行日志目录应自动创建");
        TestAssert.True(Directory.Exists(paths.Diagnostics), "诊断日志目录应自动创建");
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
