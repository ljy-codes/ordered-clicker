using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed record LegacyMigrationResult(
    bool Success,
    ClickProfile? Profile,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public static LegacyMigrationResult Migrated(
        ClickProfile profile,
        IReadOnlyList<string> warnings) =>
        new(true, profile, warnings, null);

    public static LegacyMigrationResult Failed(string error) =>
        new(false, null, [], error);
}
