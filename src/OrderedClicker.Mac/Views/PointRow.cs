using System.ComponentModel;
using System.Runtime.CompilerServices;
using OrderedClicker.Models;
namespace OrderedClicker.Mac.Views;
public sealed class PointRow : INotifyPropertyChanged
{
    public ClickPoint Point { get; }
    private readonly Action _changed;
    public PointRow(ClickPoint point, int number, Action changed) { Point = point; Number = number; _changed=changed; }
    public int Number { get; set; }
    public bool Enabled { get => Point.Enabled; set { Point.Enabled=value; Changed(); } }
    public int X { get => Point.X; set { Point.X=value; Point.RelativeX=null; Point.RelativeY=null; Changed(); } }
    public int Y { get => Point.Y; set { Point.Y=value; Point.RelativeX=null; Point.RelativeY=null; Changed(); } }
    public int ClickCount { get => Point.ClickCount; set { Point.ClickCount=value; Changed(); } }
    public int ClickIntervalMs { get => Point.ClickIntervalMs; set { Point.ClickIntervalMs=value; Changed(); } }
    public int AfterDelayMs { get => Point.AfterDelayMs; set { Point.AfterDelayMs=value; Changed(); } }
    public string Relative => Point.RelativeX is { } x && Point.RelativeY is { } y ? $"{x:P1}, {y:P1}" : "—";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name=null) { PropertyChanged?.Invoke(this,new(name)); _changed(); }
}
