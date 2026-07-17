using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed record MonitorSnapshot(
    string DeviceName,
    ScreenBounds Bounds,
    uint Dpi);
