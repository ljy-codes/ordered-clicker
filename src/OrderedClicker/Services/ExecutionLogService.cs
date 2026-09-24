using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed class ExecutionLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ExecutionLogService(string? logDirectory = null)
    {
        LogDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrderedClicker",
            "logs");
    }

    public ExecutionLogService(AppDataPaths paths)
        : this(paths?.Logs)
    {
    }

    public string LogDirectory { get; }

    public string Write(ExecutionPlan plan, ExecutionResult result)
    {
        Directory.CreateDirectory(LogDirectory);
        var path = Path.Combine(
            LogDirectory,
            $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        var temporary = path + ".tmp";
        var payload = new
        {
            CreatedAt = DateTimeOffset.Now,
            plan.TotalSourceRows,
            EnabledRows = plan.Points.Count,
            plan.DisabledSourceRowNumbers,
            plan.TotalLoops,
            plan.PlannedPointExecutionCount,
            plan.PlannedClickCount,
            result.Outcome,
            result.CompletedPointExecutionCount,
            result.CompletedClickCount,
            LastSourceRow = result.LastSourceIndex is null
                ? null
                : result.LastSourceIndex + 1,
            result.Message,
            ElapsedMilliseconds = (long)result.Elapsed.TotalMilliseconds,
            result.NextCheckpoint
        };

        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(payload, JsonOptions),
                new UTF8Encoding(false));
            File.Move(temporary, path, true);
            TryPrune();
            return path;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void TryPrune()
    {
        try
        {
            new LogRetentionService().Prune(
                LogDirectory,
                LogRetentionPolicy.Default,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
        }
    }
}
