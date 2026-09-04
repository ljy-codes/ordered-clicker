using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed record SettingsLoadResult(
    AppSettings Settings,
    string? Warning,
    string? BrokenSettingsPath);
