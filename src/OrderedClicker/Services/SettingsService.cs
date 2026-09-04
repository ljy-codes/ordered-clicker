using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Theming;

namespace OrderedClicker.Services;

public sealed class SettingsService
{
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
        if (!File.Exists(SettingsPath))
        {
            return new SettingsLoadResult(new AppSettings(), null, null);
        }

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

            return new SettingsLoadResult(settings, null, null);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            var brokenPath = MoveBrokenSettingsAside();
            return new SettingsLoadResult(
                new AppSettings(),
                $"设置文件已损坏，已恢复默认设置。原文件保存在：{brokenPath}",
                brokenPath);
        }
    }

    private string MoveBrokenSettingsAside()
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
        var directory = Path.GetDirectoryName(SettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("设置文件路径无效。");
        }

        Directory.CreateDirectory(directory);
        var temporary = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, SettingsPath, true);
        settings.RequiresSaveAfterLoad = false;
    }
}
