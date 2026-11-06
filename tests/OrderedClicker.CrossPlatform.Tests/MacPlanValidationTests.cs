using System.Runtime.InteropServices;
using OrderedClicker.Core;
using OrderedClicker.Mac.Platform;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class MacPlanValidationTests
{
    public static void Run()
    {
        var screen=new MonitorSnapshot("mac:1",new(-200,0,1400,900),192);
        var point=new ClickPoint{X=300,Y=200,MonitorDeviceName=screen.DeviceName,MonitorBounds=screen.Bounds,CapturedDpi=screen.Dpi};
        var profile=new ClickProfile{Points=[point]};
        var settings=new AppSettings();
        profile.ScreenStability.Region=new(-100,100,400,300);
        TestAssert.Equal(0,MacPlanValidation.Validate(profile,settings,[screen]).Count,"负坐标自定义区域应有效");
        var plan=ExecutionPlanService.Create(profile,screen.Bounds);
        TestAssert.Equal(profile.ScreenStability.Region!.Value,plan.StabilityRegion,"Mac 计划应使用自定义检测区域");
        TestAssert.True(plan.SourceProfile!=null,"Mac 恢复计划必须包含原始方案");
        point.X=900;
        TestAssert.Equal(300,plan.SourceProfile!.Points[0].X,"恢复快照不能跟随编辑变化");
        profile.ScreenStability.Region=new(1190,100,100,200);
        TestAssert.True(MacPlanValidation.Validate(profile,settings,[screen]).Count>0,"区域超出物理屏幕必须阻止执行");
        var separated=new[]{screen,new MonitorSnapshot("mac:2",new(1300,0,800,900),96)};
        TestAssert.True(!MacPlanValidation.IsRegionCovered(new(1100,100,300,300),separated),"区域不能跨显示器空隙");
        TestAssert.True(MacPlanValidation.IsRegionCovered(new(1000,100,300,300),
            [screen,new("mac:2",new(1200,0,800,900),96)]),"相邻显示器允许连续区域");
        TestAssert.True(!MacPlanValidation.IsRegionCovered(new(0,0,0,100),[screen]),"空区域必须拒绝");
        TestAssert.True(!MacPlanValidation.IsRegionCovered(new(int.MaxValue-5,0,100,100),[screen]),"溢出区域必须拒绝");
        profile.ScreenStability.Region=null;
        profile.TargetWindow=new(){Platform="Windows",ApplicationId="example",WindowTitle="Example"};
        TestAssert.True(MacPlanValidation.Validate(profile,settings,[screen]).Count>0,"跨平台窗口绑定应拒绝");

        var binding=new TargetWindowBinding{Platform="macOS",ApplicationId="com.example.target",WindowTitle="Target"};
        var window=new MacWindowSnapshot(binding.ApplicationId,binding.WindowTitle,101,101,false);
        TestAssert.True(MacTargetWindowGuard.Check(binding,window)==null,"绑定前台实际窗口应允许");
        TestAssert.True(MacTargetWindowGuard.Check(binding,null)!=null,"无法探测窗口必须拒绝");
        TestAssert.True(MacTargetWindowGuard.Check(binding,window with{HitProcessId=202})!=null,"前台应用相同但实际命中其他进程必须拒绝");
        TestAssert.True(MacTargetWindowGuard.Check(binding,window with{WindowTitle="Other"})!=null,"相同应用的其他窗口必须拒绝");
        TestAssert.True(MacTargetWindowGuard.Check(binding,window with{IsOwnProcess=true})!=null,"自有 HUD 遮挡必须拒绝");
        TestAssert.True(MacTargetWindowGuard.Check(binding,window with{ApplicationId=""})!=null,"缺失应用标识必须拒绝");
        TestAssert.True(MacTargetWindowGuard.Check(binding,window with{WindowTitle=""})!=null,"仅获取应用不能声称窗口检查通过");
        TestAssert.True(MacTargetWindowGuard.Check(null,null)==null,"无绑定保留兼容行为");
        TestAssert.Equal(24,Marshal.SizeOf<MacHotKeyEvent>(),"热键事件 C ABI 布局必须一致");
        TestAssert.True(typeof(MacMouseController).GetMethod(nameof(IMouseController.IsAtPosition))?.DeclaringType==typeof(MacMouseController),
            "Mac 原生适配器必须覆盖真实位置检查");
    }
}
