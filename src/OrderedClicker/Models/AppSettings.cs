using OrderedClicker.Core;
using OrderedClicker.Theming;
using System.Text.Json.Serialization;

namespace OrderedClicker.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 3;

    public AppThemeId Theme { get; set; } = AppThemeId.Aurora;

    public HotKeyBinding CaptureHotKey { get; set; } = HotKeyBindingService.DefaultCapture;

    public HotKeyBinding StartPauseHotKey { get; set; } = HotKeyBindingService.DefaultStartPause;

    public HotKeyBinding StopHotKey { get; set; } = HotKeyBindingService.DefaultStop;

    public bool SafetyCornerEnabled { get; set; } = true;

    public SafetyCorner SafetyCorner { get; set; } = SafetyCorner.TopLeft;

    public int SafetyCornerSize { get; set; } = 8;

    public int SafetyCornerDwellMs { get; set; } = 350;

    [JsonIgnore]
    public bool RequiresSaveAfterLoad { get; set; }
}
