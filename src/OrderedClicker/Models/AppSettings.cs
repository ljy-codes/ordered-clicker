using OrderedClicker.Theming;

namespace OrderedClicker.Models;

public sealed class AppSettings
{
    public AppThemeId Theme { get; set; } = AppThemeId.Aurora;
}
