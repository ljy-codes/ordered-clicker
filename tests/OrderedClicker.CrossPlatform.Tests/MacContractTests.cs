using OrderedClicker.Mac.Platform;
using OrderedClicker.Models;
using OrderedClicker.Services;
namespace OrderedClicker.Tests;
internal static class MacContractTests
{
    public static Task RunAsync()
    {
        var screen=new MonitorSnapshot("mac:1",new(0,0,1440,900),192);
        var point=new ClickPoint{X=300,Y=200,MonitorDeviceName=screen.DeviceName,MonitorBounds=screen.Bounds,CapturedDpi=screen.Dpi};
        var profile=new ClickProfile{Points=[point]};var settings=new AppSettings();
        TestAssert.Equal(0,MacPlanValidation.Validate(profile,settings,[screen]).Count,"Retina 点位有效");
        TestAssert.True(MacPlanValidation.Validate(profile,settings,[screen with {Dpi=96}]).Count>0,"缩放变化必须阻止执行");
        point.X=1;point.Y=1;
        TestAssert.True(MacPlanValidation.Validate(profile,settings,[screen]).Count>0,"安全角重叠必须阻止执行");
        point.X=300;point.Y=200;point.MonitorDeviceName="windows:1";
        TestAssert.True(MacPlanValidation.Validate(profile,settings,[screen]).Count>0,"Windows 绝对坐标必须重新采点");
        profile.CoordinateMode=CoordinateMode.CloudDesktopRegion;
        profile.CloudDesktopRegion=new(){X=100,Y=100,Width=1000,Height=700,MonitorDeviceName=screen.DeviceName,CapturedDpi=192};
        point.RelativeX=0.5;point.RelativeY=0.5;
        TestAssert.Equal(0,MacPlanValidation.Validate(profile,settings,[screen]).Count,"校准后云桌面应接受相对点位");
        TestAssert.Equal(new ScreenBounds(-1920,0,3360,1080),MacNative.VirtualBounds([screen,new("mac:2",new(-1920,0,1920,1080),96)]),"负坐标多屏范围");
        var root=Path.Combine(Path.GetTempPath(),"oc-instance-test-"+Guid.NewGuid());
        try
        {
            using(var first=new MacInstance(root))
            using(var second=new MacInstance(root))
            {TestAssert.True(first.IsOwner,"首个实例持锁");TestAssert.True(!second.IsOwner,"重复实例不能持锁");}
            using var third=new MacInstance(root);TestAssert.True(third.IsOwner,"退出后锁应释放");
        }
        finally {Directory.Delete(root,true);}
        return Task.CompletedTask;
    }
}
