namespace OrderedClicker.Theming;

public sealed record AppTheme(
    AppThemeId Id,
    string DisplayName,
    bool IsDark,
    Color Window,
    Color Surface,
    Color SurfaceAlternate,
    Color SurfaceHover,
    Color SurfacePressed,
    Color Input,
    Color Header,
    Color StatusBar,
    Color Border,
    Color GridLine,
    Color Text,
    Color MutedText,
    Color Primary,
    Color PrimaryHover,
    Color PrimaryPressed,
    Color Selection,
    Color CaptureAccent,
    Color WarningAccent,
    Color DangerAccent,
    Color SettingsAccent,
    Color HelpAccent)
{
    public Color AccentSurface(Color accent)
    {
        return Blend(Surface, accent, IsDark ? 0.20 : 0.10);
    }

    public Color AccentHover(Color accent)
    {
        return Blend(Surface, accent, IsDark ? 0.31 : 0.17);
    }

    public Color AccentPressed(Color accent)
    {
        return Blend(Surface, accent, IsDark ? 0.40 : 0.24);
    }

    public Color AccentText(Color accent)
    {
        return IsDark ? Blend(accent, Color.White, 0.25) : Blend(accent, Color.Black, 0.18);
    }

    public static Color Blend(Color background, Color foreground, double foregroundAmount)
    {
        var amount = Math.Clamp(foregroundAmount, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(background.R + (foreground.R - background.R) * amount),
            (int)Math.Round(background.G + (foreground.G - background.G) * amount),
            (int)Math.Round(background.B + (foreground.B - background.B) * amount));
    }
}
