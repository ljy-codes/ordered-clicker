using System.Text;
using System.Text.Json;
using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed record DraftEnvelope(
    ClickProfile Profile,
    string? SourcePath,
    DateTime? SourceUpdatedAtUtc,
    DateTime DraftUpdatedAtUtc,
    string? SourceSha256 = null);

public sealed class DraftService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DraftService(string draftsDirectory)
    {
        if (string.IsNullOrWhiteSpace(draftsDirectory))
        {
            throw new ArgumentException("草稿目录不能为空。", nameof(draftsDirectory));
        }

        DraftPath = Path.Combine(Path.GetFullPath(draftsDirectory), "active-draft.json");
    }

    public DraftService(AppDataPaths paths)
        : this(paths?.Drafts ?? throw new ArgumentNullException(nameof(paths)))
    {
    }

    public string DraftPath { get; }

    public bool Exists => File.Exists(DraftPath);

    public void Save(DraftEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var directory = Path.GetDirectoryName(DraftPath)
                        ?? throw new InvalidOperationException("草稿路径无效。");
        Directory.CreateDirectory(directory);
        var temporary = $"{DraftPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(envelope, JsonOptions),
                new UTF8Encoding(false));
            File.Move(temporary, DraftPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public DraftEnvelope? Load()
    {
        if (!File.Exists(DraftPath))
        {
            return null;
        }

        var json = File.ReadAllText(DraftPath, Encoding.UTF8);
        var envelope = JsonSerializer.Deserialize<DraftEnvelope>(json, JsonOptions)
                       ?? throw new InvalidDataException("草稿内容为空。");
        envelope.Profile.Points ??= [];
        envelope.Profile.ScreenStability ??= new ScreenStabilitySettings();
        return envelope;
    }

    public void Discard()
    {
        if (File.Exists(DraftPath))
        {
            File.Delete(DraftPath);
        }
    }

    public string QuarantineBrokenDraft()
    {
        if (!File.Exists(DraftPath))
        {
            return string.Empty;
        }

        var directory = Path.GetDirectoryName(DraftPath)
                        ?? throw new InvalidOperationException("草稿路径无效。");
        var brokenPath = Path.Combine(
            directory,
            $"active-draft.{DateTime.Now:yyyyMMdd-HHmmss-fff}.broken.json");
        if (File.Exists(brokenPath))
        {
            brokenPath = Path.Combine(
                directory,
                $"active-draft.{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.broken.json");
        }

        File.Move(DraftPath, brokenPath);
        return brokenPath;
    }

    public static bool HasSourceChanged(DraftEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.SourcePath))
        {
            return false;
        }

        if (envelope.SourceUpdatedAtUtc is null
            || string.IsNullOrWhiteSpace(envelope.SourceSha256)
            || !File.Exists(envelope.SourcePath))
        {
            return true;
        }

        try
        {
            return File.GetLastWriteTimeUtc(envelope.SourcePath)
                       != envelope.SourceUpdatedAtUtc.Value
                   || !string.Equals(
                       ProfileService.ComputeFileSha256(envelope.SourcePath),
                       envelope.SourceSha256,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
