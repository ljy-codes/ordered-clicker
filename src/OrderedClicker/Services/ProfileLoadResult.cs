using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed record ProfileLoadResult(
    bool Success,
    ClickProfile? Profile,
    string? ErrorMessage)
{
    public string? SourceSha256 { get; init; }

    public static ProfileLoadResult Loaded(ClickProfile profile, string? sourceSha256 = null)
        => new(true, profile, null) { SourceSha256 = sourceSha256 };

    public static ProfileLoadResult Failed(string errorMessage) => new(false, null, errorMessage);
}
