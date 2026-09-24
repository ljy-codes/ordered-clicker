namespace OrderedClicker.Services;

public sealed record LogRetentionPolicy(
    TimeSpan MaximumAge,
    int MaximumFiles,
    long MaximumBytes)
{
    public static LogRetentionPolicy Default { get; } =
        new(TimeSpan.FromDays(30), 1000, 100L * 1024 * 1024);
}

public sealed class LogRetentionService
{
    public int Prune(
        string directory,
        LogRetentionPolicy policy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.MaximumAge <= TimeSpan.Zero
            || policy.MaximumFiles < 1
            || policy.MaximumBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "日志保留限制必须为正数。");
        }

        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var removed = 0;
        var files = Directory.EnumerateFiles(directory, "*.json")
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        var cutoff = now.UtcDateTime - policy.MaximumAge;

        foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff).ToArray())
        {
            if (TryDelete(file))
            {
                files.Remove(file);
                removed++;
            }
        }

        var totalBytes = files.Sum(file => file.Exists ? file.Length : 0L);
        while (files.Count > policy.MaximumFiles || totalBytes > policy.MaximumBytes)
        {
            var oldest = files[0];
            var length = oldest.Exists ? oldest.Length : 0L;
            files.RemoveAt(0);
            if (TryDelete(oldest))
            {
                totalBytes -= length;
                removed++;
            }
        }

        return removed;
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
