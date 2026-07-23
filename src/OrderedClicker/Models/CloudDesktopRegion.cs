namespace OrderedClicker.Models;

public sealed class CloudDesktopRegion
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public string MonitorDeviceName { get; set; } = string.Empty;

    public uint CapturedDpi { get; set; } = 96;

    public ScreenBounds Bounds => new(X, Y, Width, Height);
}
