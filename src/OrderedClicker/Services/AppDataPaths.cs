namespace OrderedClicker.Services;

public sealed record AppDataPaths(
    string Root,
    string Profiles,
    string Drafts,
    string Logs,
    string Diagnostics,
    bool IsPortable)
{
    public static AppDataPaths Resolve(
        string executablePath,
        string localAppData,
        bool isPortable)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("程序路径不能为空。", nameof(executablePath));
        }

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new ArgumentException("本地应用数据目录不能为空。", nameof(localAppData));
        }

        var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))
                                  ?? throw new InvalidOperationException("程序路径无效。");
        var root = isPortable
            ? Path.Combine(executableDirectory, "OrderedClickerData")
            : Path.Combine(Path.GetFullPath(localAppData), "OrderedClicker");

        return Create(root, isPortable);
    }

    public static AppDataPaths Create(string root, bool isPortable)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var paths = new AppDataPaths(
            normalizedRoot,
            Path.Combine(normalizedRoot, "profiles"),
            Path.Combine(normalizedRoot, "drafts"),
            Path.Combine(normalizedRoot, "logs"),
            Path.Combine(normalizedRoot, "diagnostics"),
            isPortable);

        paths.EnsureWritable();
        return paths;
    }

    private void EnsureWritable()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Profiles);
        Directory.CreateDirectory(Drafts);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Diagnostics);

        var probe = Path.Combine(Root, $".write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            stream.WriteByte(0);
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }
}
