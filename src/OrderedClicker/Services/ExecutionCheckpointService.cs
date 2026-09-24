using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed class ExecutionCheckpointService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ExecutionCheckpointService(string draftsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftsDirectory);
        CheckpointPath = Path.Combine(
            Path.GetFullPath(draftsDirectory),
            "execution-checkpoint.json");
    }

    public ExecutionCheckpointService(AppDataPaths paths)
        : this(paths?.Drafts ?? throw new ArgumentNullException(nameof(paths)))
    {
    }

    public string CheckpointPath { get; }

    public void Save(ExecutionCheckpointEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.FormatVersion != 1)
        {
            throw new InvalidDataException("不支持的执行断点格式。");
        }

        var directory = Path.GetDirectoryName(CheckpointPath)
                        ?? throw new InvalidOperationException("执行断点路径无效。");
        Directory.CreateDirectory(directory);
        var temporary = $"{CheckpointPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(envelope, JsonOptions),
                new UTF8Encoding(false));
            File.Move(temporary, CheckpointPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public ExecutionCheckpointEnvelope? Load()
    {
        if (!File.Exists(CheckpointPath))
        {
            return null;
        }

        var envelope = JsonSerializer.Deserialize<ExecutionCheckpointEnvelope>(
            File.ReadAllText(CheckpointPath, Encoding.UTF8),
            JsonOptions)
            ?? throw new InvalidDataException("执行断点内容为空。");
        if (envelope.FormatVersion != 1
            || envelope.Plan is null
            || envelope.Checkpoint is null
            || string.IsNullOrWhiteSpace(envelope.PlanFingerprint)
            || !string.Equals(
                envelope.PlanFingerprint,
                envelope.Plan.ProfileFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("执行断点内容无效。");
        }

        return envelope;
    }

    public void Clear()
    {
        if (File.Exists(CheckpointPath))
        {
            File.Delete(CheckpointPath);
        }
    }

    public string QuarantineBrokenCheckpoint()
    {
        if (!File.Exists(CheckpointPath))
        {
            return string.Empty;
        }

        var directory = Path.GetDirectoryName(CheckpointPath)
                        ?? throw new InvalidOperationException("执行断点路径无效。");
        var path = Path.Combine(
            directory,
            $"execution-checkpoint.{DateTime.Now:yyyyMMdd-HHmmss-fff}."
            + $"{Guid.NewGuid():N}.broken.json");
        File.Move(CheckpointPath, path);
        return path;
    }
}
