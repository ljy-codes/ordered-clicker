using OrderedClicker.Models;
using OrderedClicker.Services;
using OrderedClicker.Theming;

namespace OrderedClicker.Tests;

internal static class ThemeSettingsTests
{
    public static void Run()
    {
        ThemeCatalogContainsFiveReadableThemes();
        SettingsRoundTripPreservesTheme();
        MissingOrInvalidSettingsUseAurora();
    }

    private static void ThemeCatalogContainsFiveReadableThemes()
    {
        TestAssert.Equal(5, AppThemeCatalog.All.Count, "应提供五套预设主题");
        TestAssert.Equal(
            5,
            AppThemeCatalog.All.Select(theme => theme.Id).Distinct().Count(),
            "每套主题标识应唯一");

        foreach (var theme in AppThemeCatalog.All)
        {
            TestAssert.True(
                ContrastRatio(theme.Text, theme.Window) >= 4.5,
                $"{theme.DisplayName} 的正文与窗口背景对比度应清晰");
            TestAssert.True(
                ContrastRatio(theme.Text, theme.Surface) >= 4.5,
                $"{theme.DisplayName} 的正文与内容表面对比度应清晰");
        }
    }

    private static void SettingsRoundTripPreservesTheme()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var service = new SettingsService(Path.Combine(directory, "settings.json"));
            service.Save(new AppSettings { Theme = AppThemeId.Emerald });

            var loaded = service.Load();

            TestAssert.Equal(AppThemeId.Emerald, loaded.Theme, "设置保存后应恢复主题");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void MissingOrInvalidSettingsUseAurora()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var service = new SettingsService(path);
            TestAssert.Equal(AppThemeId.Aurora, service.Load().Theme,
                "设置文件不存在时应使用极光科技主题");

            File.WriteAllText(path, "{ invalid json");
            TestAssert.Equal(AppThemeId.Aurora, service.Load().Theme,
                "设置文件损坏时应使用极光科技主题");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"OrderedClicker.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Convert(byte component)
        {
            var value = component / 255d;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Convert(color.R)
               + 0.7152 * Convert(color.G)
               + 0.0722 * Convert(color.B);
    }
}
