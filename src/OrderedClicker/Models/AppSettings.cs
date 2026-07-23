using OrderedClicker.Core;
using OrderedClicker.Theming;
using System.Text.Json.Serialization;

namespace OrderedClicker.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 2;

    public AppThemeId Theme { get; set; } = AppThemeId.Aurora;

    public HotKeyBinding CaptureHotKey { get; set; } = HotKeyBindingService.DefaultCapture;

    public HotKeyBinding StartPauseHotKey { get; set; } = HotKeyBindingService.DefaultStartPause;

    public HotKeyBinding StopHotKey { get; set; } = HotKeyBindingService.DefaultStop;

    [JsonIgnore]
    public bool RequiresSaveAfterLoad { get; set; }
}
