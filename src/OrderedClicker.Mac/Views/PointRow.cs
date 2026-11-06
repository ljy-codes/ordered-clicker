using System.ComponentModel;
using System.Runtime.CompilerServices;
using OrderedClicker.Core;
using OrderedClicker.Models;
namespace OrderedClicker.Mac.Views;
public sealed class PointRow : INotifyPropertyChanged
{
    public ClickPoint Point { get; }
    private readonly Action _changed;
    private readonly Func<CloudDesktopRegion?> _region;
    public PointRow(ClickPoint point, int number, Action changed, Func<CloudDesktopRegion?>? region = null)
    { Point = point; Number = number; _changed=changed; _region=region??(()=>null); }
    public int Number { get; set; }
    public bool Enabled { get => Point.Enabled; set { Point.Enabled=value; Changed(); } }
    public int X { get => Point.X; set { if(Point.X==value)return; Point.X=value; CoordinatesChanged(); } }
    public int Y { get => Point.Y; set { if(Point.Y==value)return; Point.Y=value; CoordinatesChanged(); } }
    public int ClickCount { get => Point.ClickCount; set { Point.ClickCount=value; Changed(); } }
    public int ClickIntervalMs { get => Point.ClickIntervalMs; set { Point.ClickIntervalMs=value; Changed(); } }
    public int AfterDelayMs { get => Point.AfterDelayMs; set { Point.AfterDelayMs=value; Changed(); } }
    public string Relative => Point.RelativeX is { } x && Point.RelativeY is { } y ? $"{x:P1}, {y:P1}" : "—";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void CoordinatesChanged([CallerMemberName] string? name=null)
    {
        Point.RelativeX=null; Point.RelativeY=null;
        var region=_region();
        if(region is { Width: >=100, Height: >=100 } && region.Bounds.Contains(Point.X,Point.Y))
        {
            var relative=CloudDesktopCoordinateService.ToRelative(region,Point.X,Point.Y);
            Point.RelativeX=relative.X; Point.RelativeY=relative.Y;
        }
        PropertyChanged?.Invoke(this,new(nameof(Relative)));
        Changed(name);
    }
    private void Changed([CallerMemberName] string? name=null) { PropertyChanged?.Invoke(this,new(name)); _changed(); }
}
