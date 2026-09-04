using System.Text;
using System.Text.Json;
using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed class LegacyProfileMigrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LegacyMigrationResult Migrate(string sourcePath)
    {
        try
        {
            var json = File.ReadAllText(sourcePath, Encoding.UTF8);
            using var document = JsonDocument.Parse(json);
            if (!TryReadVersion(document.RootElement, out var version)
                || version is < 1 or > 3)
            {
                return LegacyMigrationResult.Failed(
                    "只支持迁移版本 1 至 3 的旧 JSON 方案。");
            }

            var profile = JsonSerializer.Deserialize<ClickProfile>(json, JsonOptions);
            if (profile is null)
            {
                return LegacyMigrationResult.Failed("旧方案内容为空。");
            }

            profile.Points ??= [];
            profile.ScreenStability ??= new ScreenStabilitySettings();
            for (var index = 0; index < profile.Points.Count; index++)
            {
                if (profile.Points[index] is null)
                {
                    return LegacyMigrationResult.Failed(
                        $"旧方案数据无效：点位 {index + 1} 内容为空。");
                }
            }

            var warnings = NormalizeCloudCoordinates(profile);
            var now = DateTime.UtcNow;
            profile.FormatVersion = 4;
            profile.ProfileId = Guid.NewGuid();
            profile.CreatedAtUtc = now;
            profile.UpdatedAtUtc = now;
            var validationError = ProfileService.ValidateProfile(profile);
            if (validationError is not null)
            {
                return LegacyMigrationResult.Failed(
                    $"旧方案数据无效：{validationError}");
            }

            return LegacyMigrationResult.Migrated(profile, warnings);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return LegacyMigrationResult.Failed($"迁移失败：{exception.Message}");
        }
    }

    private static bool TryReadVersion(JsonElement root, out int version)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("Version", StringComparison.OrdinalIgnoreCase)
                && property.Value.TryGetInt32(out version))
            {
                return true;
            }
        }

        version = 0;
        return false;
    }

    private static IReadOnlyList<string> NormalizeCloudCoordinates(ClickProfile profile)
    {
        var warnings = new List<string>();
        if (profile.CoordinateMode != CoordinateMode.CloudDesktopRegion
            || profile.CloudDesktopRegion is null)
        {
            return warnings;
        }

        for (var index = 0; index < profile.Points.Count; index++)
        {
            var point = profile.Points[index];
            if (point.RelativeX is not null && point.RelativeY is not null)
            {
                continue;
            }

            try
            {
                var relative = CloudDesktopCoordinateService.ToRelative(
                    profile.CloudDesktopRegion,
                    point.X,
                    point.Y);
                point.RelativeX = relative.X;
                point.RelativeY = relative.Y;
                warnings.Add($"点位 {index + 1} 的相对坐标已根据旧区域重新计算。");
            }
            catch (ArgumentOutOfRangeException)
            {
                warnings.Add($"点位 {index + 1} 无法补算相对坐标，请迁移后重新采点。");
            }
        }

        return warnings;
    }
}
