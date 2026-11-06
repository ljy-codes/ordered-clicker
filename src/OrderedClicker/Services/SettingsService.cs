using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Theming;

namespace OrderedClicker.Services;

public sealed class SettingsService
{
    private bool _preserveOriginal;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SettingsService(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrderedClicker",
            "settings.json");
    }

    public SettingsService(AppDataPaths paths)
        : this(Path.Combine(
            paths?.Root ?? throw new ArgumentNullException(nameof(paths)),
            "settings.json"))
    {
    }

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        return LoadWithResult().Settings;
    }

    public SettingsLoadResult LoadWithResult()
    {
        try
        {
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null || !Enum.IsDefined(settings.Theme))
            {
                throw new JsonException("设置内容为空或主题无效。");
            }

            if (settings.Version < 2)
            {
                if (settings.CaptureHotKey == HotKeyBindingService.CompatibilityCapture
                    && settings.StartPauseHotKey
                    == HotKeyBindingService.CompatibilityStartPause
                    && settings.StopHotKey == HotKeyBindingService.CompatibilityStop)
                {
                    settings.CaptureHotKey = HotKeyBindingService.DefaultCapture;
                    settings.StartPauseHotKey = HotKeyBindingService.DefaultStartPause;
                    settings.StopHotKey = HotKeyBindingService.DefaultStop;
                }

                settings.Version = 2;
                settings.RequiresSaveAfterLoad = true;
            }

            if (settings.Version < 3)
            {
                settings.Version = 3;
                settings.SafetyCornerEnabled = true;
                settings.SafetyCorner = SafetyCorner.TopLeft;
                settings.SafetyCornerSize = 8;
                settings.SafetyCornerDwellMs = 350;
                settings.RequiresSaveAfterLoad = true;
            }

            if (!Enum.IsDefined(settings.SafetyCorner)
                || settings.SafetyCornerSize is < 4 or > 64
                || settings.SafetyCornerDwellMs is < 100 or > 3000)
            {
                settings.SafetyCornerEnabled = true;
                settings.SafetyCorner = SafetyCorner.TopLeft;
                settings.SafetyCornerSize = 8;
                settings.SafetyCornerDwellMs = 350;
                settings.RequiresSaveAfterLoad = true;
            }

            if (!HotKeyBindingService.ValidateSet(
                    settings.CaptureHotKey,
                    settings.StartPauseHotKey,
                    settings.StopHotKey,
                    out _))
            {
                settings.CaptureHotKey = HotKeyBindingService.DefaultCapture;
                settings.StartPauseHotKey = HotKeyBindingService.DefaultStartPause;
                settings.StopHotKey = HotKeyBindingService.DefaultStop;
            }

            _preserveOriginal = false;
            return new SettingsLoadResult(settings, null, null);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            _preserveOriginal = false;
            return new SettingsLoadResult(new AppSettings(), null, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _preserveOriginal = true;
            return new SettingsLoadResult(
                new AppSettings(),
                $"暂时无法读取设置文件，已使用默认设置；原文件保持不变，请重新加载后再保存。原因：{exception.Message}",
                null);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            var brokenPath = MoveBrokenSettingsAside();
            _preserveOriginal = brokenPath is null;
            return new SettingsLoadResult(
                new AppSettings(),
                brokenPath is null
                    ? "设置文件内容无效，已使用默认设置；无法隔离原文件，已保留原路径，请重新加载后再保存。"
                    : $"设置文件已损坏，已恢复默认设置。原文件保存在：{brokenPath}",
                brokenPath);
        }
    }

    private string? MoveBrokenSettingsAside()
    {
        try
        {
            return MoveBrokenSettingsAsideCore();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string MoveBrokenSettingsAsideCore()
    {
        var directory = Path.GetDirectoryName(SettingsPath)
                        ?? throw new InvalidOperationException("设置文件路径无效。");
        Directory.CreateDirectory(directory);
        var brokenPath = Path.Combine(
            directory,
            $"settings.{DateTime.Now:yyyyMMdd-HHmmss-fff}.broken.json");
        if (File.Exists(brokenPath))
        {
            brokenPath = Path.Combine(
                directory,
                $"settings.{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.broken.json");
        }

        File.Move(SettingsPath, brokenPath);
        return brokenPath;
    }

    public void Save(AppSettings settings)
    {
        if (_preserveOriginal)
        {
            throw new IOException("上次读取设置失败，原文件已受到保护；请成功重新加载后再保存。");
        }

        var directory = Path.GetDirectoryName(SettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("设置文件路径无效。");
        }

        Directory.CreateDirectory(directory);
        var temporary = $"{SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, SettingsPath, true);
            settings.RequiresSaveAfterLoad = false;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
