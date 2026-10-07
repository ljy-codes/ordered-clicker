using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OrderedClicker.Models;
namespace OrderedClicker.Mac.Views;
public sealed partial class MainWindow
{
    private async Task RenderPreview()
    {
        Directory.CreateDirectory(Program.RenderDirectory!);
        var example=new ClickProfile{Name="日常流程",TotalLoops=3,Points=[
            new(){X=320,Y=240,ClickCount=1,ClickIntervalMs=100,AfterDelayMs=500},
            new(){X=640,Y=380,ClickCount=2,ClickIntervalMs=150,AfterDelayMs=800},
            new(){X=860,Y=520,ClickCount=1,ClickIntervalMs=100,AfterDelayMs=1000}]};
        ApplyProfile(example,null,true);_permission.Text="辅助功能：未授权    屏幕录制：未授权\n运行前请完成系统授权。";
        for(var index=0;index<5;index++)
        {
            ShowPage(index);await Task.Delay(350);
            using var image=new RenderTargetBitmap(new PixelSize((int)ClientSize.Width,(int)ClientSize.Height),new Vector(96,96));
            image.Render(this);image.Save(Path.Combine(Program.RenderDirectory!,$"mac-page-{index+1}.png"));
        }
        if(Program.NativeProbe)
        {
            var initialized=Platform.MacNative.oc_initialize();
            var displays=Platform.MacNative.Displays();
            var cursor=Platform.MacNative.Cursor();
            using var registrar=new Platform.MacHotKeyRegistrar();
            var keys=new[]{OrderedClicker.Platform.ShortcutKey.F16,OrderedClicker.Platform.ShortcutKey.F17,OrderedClicker.Platform.ShortcutKey.F18};
            for(var i=0;i<keys.Length;i++)registrar.Register(new(i+1,"验证",new(keys[i],ShortcutModifiers.Control|ShortcutModifiers.Alt)));
            for(var i=1;i<=3;i++)registrar.Unregister(i);
            var report=new { Initialized=initialized==1, DisplayCount=displays.Count, Displays=displays,
                CursorRead=true, HotKeyRegistration=true, AccessibilityGranted=Platform.MacNative.oc_accessibility(0)==1,
                ScreenCaptureGranted=Platform.MacNative.oc_screen_permission(0)==1,
                ActualInputAndScreenCaptureTested=false };
            File.WriteAllText(Path.Combine(Program.RenderDirectory!,"native-probe.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }
        _allowClose=true;Close();
    }
}
