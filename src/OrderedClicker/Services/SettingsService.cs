using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null || !Enum.IsDefined(settings.Theme))
            {
                return new AppSettings();
            }

            return settings;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return new AppSettings();
        }
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
    }
}
