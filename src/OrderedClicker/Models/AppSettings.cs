using OrderedClicker.Core;
using OrderedClicker.Theming;

namespace OrderedClicker.Models;

public sealed class AppSettings
{
    public AppThemeId Theme { get; set; } = AppThemeId.Aurora;

    public HotKeyBinding CaptureHotKey { get; set; } = HotKeyBindingService.DefaultCapture;

    public HotKeyBinding StartPauseHotKey { get; set; } = HotKeyBindingService.DefaultStartPause;

    public HotKeyBinding StopHotKey { get; set; } = HotKeyBindingService.DefaultStop;
}
