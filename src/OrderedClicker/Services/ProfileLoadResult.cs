using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed record ProfileLoadResult(
    bool Success,
    ClickProfile? Profile,
    string? ErrorMessage)
{
    public static ProfileLoadResult Loaded(ClickProfile profile) => new(true, profile, null);

    public static ProfileLoadResult Failed(string errorMessage) => new(false, null, errorMessage);
}
