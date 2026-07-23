using System.Text;
using System.Text.Json;
using OrderedClicker.Core;
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
        return Save(profile, destination);
    }

    public string Save(ClickProfile profile, string destination)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("保存路径不能为空。", nameof(destination));
        }

        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("保存路径无效。");
        }

        Directory.CreateDirectory(directory);
        var temporary = destination + ".tmp";
        var json = JsonSerializer.Serialize(profile, JsonOptions);

        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, destination, true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public string Export(ClickProfile profile, string destination)
    {
        return Save(profile, destination);
    }

    public ProfileLoadResult Import(string path)
    {
        var result = Load(path);
        if (!result.Success)
        {
            return result;
        }

        var profile = result.Profile!;
        var validationError = ValidateImportedProfile(profile);
        return validationError is null
            ? ProfileLoadResult.Loaded(profile)
            : ProfileLoadResult.Failed(validationError);
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
            profile.ScreenStability ??= new ScreenStabilitySettings();
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

    private static string? ValidateImportedProfile(ClickProfile profile)
    {
        if (profile.Version is < 1 or > 3)
        {
            return $"不支持方案文件版本 {profile.Version}，当前支持版本 1 至 3。";
        }

        if (profile.TotalLoops is < 1 or > 100000)
        {
            return "总循环次数必须在 1 到 100000 之间。";
        }

        if (profile.LoopDelayMs is < 0 or > 600000)
        {
            return "轮间等待必须在 0 到 600000 毫秒之间。";
        }

        if (profile.DefaultClickIntervalMs is < 10 or > 600000)
        {
            return "默认点击间隔必须在 10 到 600000 毫秒之间。";
        }

        if (profile.DefaultAfterDelayMs is < 0 or > 600000)
        {
            return "默认点后等待必须在 0 到 600000 毫秒之间。";
        }

        if (!Enum.IsDefined(profile.CoordinateMode))
        {
            return "方案文件包含不支持的坐标模式。";
        }

        if (profile.CoordinateMode == CoordinateMode.CloudDesktopRegion)
        {
            try
            {
                CloudDesktopCoordinateService.ValidateRegion(
                    profile.CloudDesktopRegion
                    ?? throw new ArgumentOutOfRangeException(nameof(profile.CloudDesktopRegion)));
            }
            catch (ArgumentOutOfRangeException)
            {
                return "云桌面区域无效，宽度和高度不能小于 100 像素。";
            }
        }

        for (var index = 0; index < profile.Points.Count; index++)
        {
            var point = profile.Points[index];
            var displayIndex = index + 1;
            if (point.ClickCount is < 1 or > 100000)
            {
                return $"点位 {displayIndex} 的点击次数必须在 1 到 100000 之间。";
            }

            if (point.ClickIntervalMs is < 10 or > 600000)
            {
                return $"点位 {displayIndex} 的点击间隔必须在 10 到 600000 毫秒之间。";
            }

            if (point.AfterDelayMs is < 0 or > 600000)
            {
                return $"点位 {displayIndex} 的点后等待必须在 0 到 600000 毫秒之间。";
            }

            if (profile.CoordinateMode != CoordinateMode.CloudDesktopRegion)
            {
                continue;
            }

            if (point.RelativeX is null || point.RelativeY is null)
            {
                return $"点位 {displayIndex} 缺少云桌面相对坐标。";
            }

            try
            {
                CloudDesktopCoordinateService.ValidateRelative(
                    point.RelativeX.Value,
                    point.RelativeY.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                return $"点位 {displayIndex} 的云桌面相对坐标必须在 0 到 1 之间。";
            }
        }

        return null;
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
