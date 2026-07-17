using System.Text;
using System.Text.Json;
using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed class ProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProfileService(string? profilesDirectory = null)
    {
        ProfilesDirectory = profilesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrderedClicker",
            "profiles");
    }

    public string ProfilesDirectory { get; }

    public string Save(ClickProfile profile)
    {
        Directory.CreateDirectory(ProfilesDirectory);
        var fileName = SanitizeFileName(profile.Name);
        var destination = Path.Combine(ProfilesDirectory, $"{fileName}.json");
        var temporary = destination + ".tmp";
        var json = JsonSerializer.Serialize(profile, JsonOptions);

        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, destination, true);
        return destination;
    }

    public ProfileLoadResult Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var profile = JsonSerializer.Deserialize<ClickProfile>(json, JsonOptions);
            if (profile is null)
            {
                return ProfileLoadResult.Failed("配置文件内容为空。");
            }

            profile.Points ??= [];
            return ProfileLoadResult.Loaded(profile);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return ProfileLoadResult.Failed($"读取配置失败：{exception.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "默认方案" : name.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter, '_');
        }

        return value;
    }
}
