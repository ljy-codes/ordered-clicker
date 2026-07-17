using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed record CapturedPoint(
    int X,
    int Y,
    string MonitorDeviceName,
    ScreenBounds MonitorBounds,
    uint Dpi);
