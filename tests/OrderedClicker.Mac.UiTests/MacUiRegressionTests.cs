using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OrderedClicker.Core;
using OrderedClicker.Mac.Platform;
using OrderedClicker.Mac.Views;
using OrderedClicker.Models;
using OrderedClicker.Services;

internal static class MacUiRegressionTests
{
    private static readonly MonitorSnapshot Display=new("mac:1",new(0,0,1440,900),192);

    public static void Run(string root)
    {
        var tests=new (string Name,Action Run)[]
        {
            ("relative coordinate edits use current calibration",CoordinateEdits),
            ("unfinished draft and SourceProfile recovery",()=>Recovery(root)),
            ("ordered press-time capture and HUD replacement",()=>Capture(root)),
            ("custom stability region capture and reset",()=>StabilityRegion(root)),
            ("delayed target-window binding and clear",()=>WindowBinding(root)),
            ("rehearsal preserves normal checkpoint and draft",()=>Rehearsal(root)),
            ("step checkpoint and cancellable resume countdown",()=>StepAndResume(root)),
            ("per-input display, own-UI and window guards",()=>InputGuards(root)),
            ("initial countdown cancellation",()=>CancelCountdown(root))
        };
        foreach(var test in tests)
        {
            test.Run();Console.WriteLine("PASS "+test.Name);
        }
    }

    private static void CoordinateEdits()
    {
        CloudDesktopRegion region=new(){X=100,Y=100,Width=1000,Height=600};
        var point=new ClickPoint{X=200,Y=200,RelativeX=.1,RelativeY=1d/6};
        var changes=new List<string?>();
        var row=new PointRow(point,1,()=>{},()=>region);
        row.PropertyChanged+=(_,e)=>changes.Add(e.PropertyName);
        row.X=400;row.Y=400;
        Assert(point.RelativeX==.3&&point.RelativeY==.5,"编辑 X/Y 后必须重新计算相对坐标");
        region=new(){X=0,Y=0,Width=800,Height=800};
        row.X=600;
        Assert(point.RelativeX==.75&&point.RelativeY==.5,"编辑必须读取最新校准上下文");
        row.Y=900;
        Assert(point.RelativeX==null&&point.RelativeY==null,"区域外编辑应留为可修复的未完成状态");
        row.Y=400;
        Assert(point.RelativeX==.75&&point.RelativeY==.5&&changes.Contains(nameof(PointRow.Relative)),
            "修正后应恢复相对坐标并通知显示");
    }

    private static void Recovery(string root)
    {
        using(var h=new Harness(root))
        {
            var unfinished=new ClickProfile
            {
                Name="未完成校准",CoordinateMode=CoordinateMode.CloudDesktopRegion,
                Points=[new(){X=300,Y=200,RelativeX=null,RelativeY=null}]
            };
            new DraftService(h.Paths).Save(new(unfinished,null,null,DateTime.UtcNow));
            Invoke(h.Window,"RestoreDraftAndCheckpoint");
            Assert(h.Snapshot().Name==unfinished.Name&&h.Snapshot().CloudDesktopRegion==null,"未完成草稿必须恢复");
            Assert(Directory.GetFiles(h.Paths.Drafts,"*.broken.json").Length==0,"逻辑未完成不应隔离为损坏");
        }
        using(var h=new Harness(root))
        {
            var source=Profile();
            source.Name="原始断点方案";
            source.Points.Add(new(){X=777,Y=555,Enabled=false,ClickCount=9});
            source.ScreenStability.Region=new(100,100,400,300);
            source.TargetWindow=Binding();
            var plan=ExecutionPlanService.Create(source,Display.Bounds);
            new ExecutionCheckpointService(h.Paths).Save(Envelope(plan,ExecutionCheckpoint.Start));
            new DraftService(h.Paths).Save(new(new ClickProfile{Name="其他草稿"},null,null,DateTime.UtcNow));
            Invoke(h.Window,"RestoreDraftAndCheckpoint");
            var restored=h.Snapshot();
            Assert(restored.Name==source.Name&&restored.Points.Count==3&&!restored.Points[2].Enabled,"SourceProfile 应优先恢复完整编辑行");
            Assert(restored.ScreenStability.Region==source.ScreenStability.Region&&restored.TargetWindow?.WindowTitle=="Target",
                "恢复必须保留检测区域和窗口绑定");
            Assert(ExecutionPlanFingerprintService.Create(restored)==plan.ProfileFingerprint,"恢复后指纹必须匹配原始方案");
        }
    }

    private static void Capture(string root)
    {
        using var h=new Harness(root);
        Invoke(h.Window,"BeginCapture");
        var oldHud=Get<Window>(h.Window,"_hud");
        Invoke(h.Window,"BeginCalibration");
        Assert(Get<CaptureMode>(h.Window,"_captureMode")==CaptureMode.CloudRegionTopLeft,"替换 HUD 不能触发旧回调取消新采集");
        Assert(!h.Window.IsVisible&&!ReferenceEquals(oldHud,Get<Window>(h.Window,"_hud")),"新采集必须保留隐藏状态和新 HUD");
        Invoke(h.Window,"EndCapture");
        Invoke(h.Window,"BeginCapture");
        h.Events.Enqueue(new(1,1,310,220,1));h.Events.Enqueue(new(2,1,620,440,1));
        Invoke(h.Window,"Tick");
        var points=h.Snapshot().Points;
        Assert(points.Count==2&&points[0].X==310&&points[0].Y==220&&points[1].X==620&&points[1].Y==440,
            "同次轮询中的两次按键必须按当时坐标分别记录");
        h.Events.Enqueue(new(2,1,999,800,1));Invoke(h.Window,"Tick");
        Assert(h.Snapshot().Points.Count==2,"旧序号不能被重复采集");
        h.Events.Enqueue(new(3,3,0,0,0));h.Events.Enqueue(new(4,1,900,700,1));Invoke(h.Window,"Tick");
        Assert(Get<CaptureMode>(h.Window,"_captureMode")==CaptureMode.Idle&&h.Snapshot().Points.Count==2,
            "停止键之后的同批采点不能继续执行");
        Assert(h.Mouse.Moves==0&&h.Mouse.Clicks==0,"采集回归不能发送鼠标输入");
    }

    private static void StabilityRegion(string root)
    {
        using var h=new Harness(root);
        Invoke(h.Window,"BeginStabilityRegion");
        h.Events.Enqueue(new(1,1,100,120,1));h.Events.Enqueue(new(2,1,500,420,1));Invoke(h.Window,"Tick");
        Assert(h.Snapshot().ScreenStability.Region==new ScreenBounds(100,120,400,300),"双角热键必须记录检测区域");
        Invoke(h.Window,"ShowPage",3);h.Click("重置检测区域");
        Assert(h.Snapshot().ScreenStability.Region==null,"重置应恢复自动区域");
    }

    private static void WindowBinding(string root)
    {
        using var h=new Harness(root);
        var task=(Task)Invoke(h.Window,"CaptureTargetWindow")!;
        Assert(h.Probes==0&&!h.Window.IsVisible,"绑定必须先隐藏自身并延时，让用户选择实际应用");
        Wait(task);
        Assert(h.Probes==1&&h.Snapshot().TargetWindow?.ApplicationId=="com.example.target","延时结束应绑定实际窗口");
        Invoke(h.Window,"ShowPage",3);h.Click("清除绑定");
        Assert(h.Snapshot().TargetWindow==null,"清除应使用 null 表示无绑定");
        var cancel=(Task)Invoke(h.Window,"CaptureTargetWindow")!;
        Invoke(h.Window,"EndCapture");Wait(cancel);
        Assert(h.Probes==1,"取消延时绑定后不得再读取窗口");
    }

    private static void Rehearsal(string root)
    {
        using var h=new Harness(root);
        var normal=Profile();normal.Name="正常任务";
        var normalPlan=ExecutionPlanService.Create(normal,Display.Bounds);
        var recovery=Envelope(normalPlan,ExecutionCheckpoint.Start);
        new ExecutionCheckpointService(h.Paths).Save(recovery);
        new DraftService(h.Paths).Save(new(normal,null,null,DateTime.UtcNow));
        Set(h.Window,"_recovery",recovery);
        var checkpointBytes=File.ReadAllBytes(new ExecutionCheckpointService(h.Paths).CheckpointPath);
        var draftBytes=File.ReadAllBytes(new DraftService(h.Paths).DraftPath);
        var rehearsal=Profile();rehearsal.Name="另一个演练方案";
        h.Start(rehearsal,ExecutionRunMode.Rehearsal);Wait(h.Execution);
        Assert(h.Mouse.Moves==2&&h.Mouse.Clicks==0&&h.Mouse.Ups==0,"演练只能每点移动一次，不能点击或补发鼠标抬起");
        Assert(checkpointBytes.SequenceEqual(File.ReadAllBytes(new ExecutionCheckpointService(h.Paths).CheckpointPath))&&
            draftBytes.SequenceEqual(File.ReadAllBytes(new DraftService(h.Paths).DraftPath)),"演练不能替换、清除正常断点或草稿");
        Assert(ReferenceEquals(Get<ExecutionCheckpointEnvelope>(h.Window,"_recovery"),recovery),"演练不能覆盖内存中的正常断点");
    }

    private static void StepAndResume(string root)
    {
        using var h=new Harness(root);
        h.Start(Profile(),ExecutionRunMode.Step);
        Until(()=>Get<AsyncPauseGate>(h.Window,"_pause").IsPaused,()=>Invoke(h.Window,"Tick"));
        Invoke(h.Window,"Tick");
        Assert(h.Mouse.Clicks==2&&h.Window.IsVisible,"逐点模式完成首点全部点击后必须暂停并显示操作窗口");
        var saved=new ExecutionCheckpointService(h.Paths).Load()!;
        Assert(saved.Checkpoint.CompletedClickCount==2&&saved.Checkpoint.PointIndex==1,"逐点暂停必须持久化正常进度到下一点");
        var moves=h.Mouse.Moves;
        h.BlockCountdown=true;
        var resume=(Task)Invoke(h.Window,"StartPause")!;
        Assert(Get<ExecutionState>(h.Window,"_state")==ExecutionState.Countdown&&!h.Window.IsVisible,
            "继续必须重新倒计时并隐藏主窗口");
        Invoke(h.Window,"Stop","测试取消恢复");Wait(resume);Wait(h.Execution);
        Assert(h.Mouse.Clicks==2&&h.Mouse.Moves==moves,"恢复倒计时取消不得定位或点击下一点");

        using var complete=new Harness(root);
        complete.Start(Profile(),ExecutionRunMode.Step);
        Until(()=>Get<AsyncPauseGate>(complete.Window,"_pause").IsPaused,()=>Invoke(complete.Window,"Tick"));
        Invoke(complete.Window,"Tick");
        Wait((Task)Invoke(complete.Window,"StartPause")!);Wait(complete.Execution);
        Assert(complete.Mouse.Clicks==3&&!File.Exists(new ExecutionCheckpointService(complete.Paths).CheckpointPath),
            "每次继续只能执行下一点，全部完成后清理正常断点");
    }

    private static void InputGuards(string root)
    {
        foreach(var guard in new[]{"display","permission","own","unknown","window"})
        {
            using var h=new Harness(root);
            var profile=Profile();profile.TargetWindow=Binding();
            if(guard=="display")h.CurrentDisplays=[Display with{Dpi=96}];
            if(guard=="permission")h.Accessibility=false;
            if(guard=="own")h.OwnUi=1;
            if(guard=="unknown")h.OwnUi=-1;
            if(guard=="window")h.ProbedWindow=h.ProbedWindow with{HitProcessId=202};
            h.Start(profile,ExecutionRunMode.Normal);
            if(guard is "display" or "permission")
            {
                Wait(h.Execution);
            }
            else
            {
                Until(()=>Get<AsyncPauseGate>(h.Window,"_pause").IsPaused,()=>Invoke(h.Window,"Tick"));
                Invoke(h.Window,"Stop","测试结束");Wait(h.Execution);
            }
            Assert(h.Mouse.Clicks==0&&h.Mouse.Moves==0,"安全检查失效必须在输入前阻止："+guard);
        }
        using(var h=new Harness(root))
        {
            var profile=Profile();profile.TargetWindow=Binding();
            h.Mouse.AfterClick=()=>h.ProbedWindow=h.ProbedWindow with{WindowTitle="Covered"};
            h.Start(profile,ExecutionRunMode.Normal);
            Until(()=>Get<AsyncPauseGate>(h.Window,"_pause").IsPaused,()=>Invoke(h.Window,"Tick"));
            Assert(h.Mouse.Clicks==1,"每次点击前必须重新校验实际窗口");
            Invoke(h.Window,"Stop","测试结束");Wait(h.Execution);
        }
        using(var h=new Harness(root))
        {
            h.UseProfile(Profile());h.Window.Show();
            var plan=ExecutionPlanService.Create(h.Snapshot(),Display.Bounds);
            var reason=(string?)Invoke(h.Window,"CheckInputSafety",plan,new[]{Display},plan.Points[0]);
            Assert(reason!=null,"主窗口可见时必须拒绝输入");
        }
    }

    private static void CancelCountdown(string root)
    {
        using var h=new Harness(root){BlockCountdown=true};
        h.Start(Profile(),ExecutionRunMode.Normal);
        Assert(!h.Window.IsVisible&&h.Mouse.Moves==0,"初始倒计时期间必须隐藏主窗口且没有输入");
        Invoke(h.Window,"Stop","取消初始倒计时");Wait(h.Execution);
        Assert(h.Mouse.Moves==0&&h.Mouse.Clicks==0&&h.Mouse.Ups==0,"取消初始倒计时不能发送任何鼠标输入");
    }

    private static ClickProfile Profile()=>new()
    {
        Name="测试方案",LoopDelayMs=0,Points=[
            new(){X=300,Y=220,ClickCount=2,ClickIntervalMs=10,AfterDelayMs=0,MonitorDeviceName=Display.DeviceName,MonitorBounds=Display.Bounds,CapturedDpi=Display.Dpi},
            new(){X=600,Y=440,ClickCount=1,ClickIntervalMs=10,AfterDelayMs=0,MonitorDeviceName=Display.DeviceName,MonitorBounds=Display.Bounds,CapturedDpi=Display.Dpi}]
    };
    private static TargetWindowBinding Binding()=>new(){Platform="macOS",ApplicationId="com.example.target",WindowTitle="Target"};
    private static ExecutionCheckpointEnvelope Envelope(ExecutionPlan plan,ExecutionCheckpoint checkpoint)=>
        new(1,"test-mac",plan,plan.ProfileFingerprint,checkpoint,DateTimeOffset.UtcNow);
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static object? Invoke(object target,string name,params object?[] args)=>
        target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
    private static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,value);
    private static void Until(Func<bool> done,Action? poll=null)
    {
        var clock=Stopwatch.StartNew();
        while(!done())
        {
            Dispatcher.UIThread.RunJobs();poll?.Invoke();
            if(clock.Elapsed>TimeSpan.FromSeconds(8))throw new TimeoutException("托管 UI 操作未在预期时间内完成。");
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Wait(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}

    private sealed class Harness:IDisposable
    {
        public readonly AppDataPaths Paths;
        public readonly MainWindow Window;
        public readonly FakeMouse Mouse=new();
        public readonly Queue<MacHotKeyEvent> Events=new();
        public IReadOnlyList<MonitorSnapshot> CurrentDisplays=[Display];
        public bool Accessibility=true,BlockCountdown;
        public int OwnUi,Probes;
        public MacWindowSnapshot ProbedWindow=new("com.example.target","Target",101,101,false);
        public Harness(string root)
        {
            Paths=AppDataPaths.Create(Path.Combine(root,Guid.NewGuid().ToString("N")),false);
            var platform=new MacPlatformServices
            {
                Displays=()=>CurrentDisplays,Cursor=()=>new(350,250),Accessibility=()=>Accessibility,ScreenPermission=()=>true,
                OwnUiAt=(_,_)=>OwnUi,ProbeWindow=(_,_)=>{Probes++;return ProbedWindow;},
                CreateMouse=()=>Mouse,PollEvents=()=>0,
                CountdownDelay=(_,token)=>BlockCountdown?Task.Delay(Timeout.InfiniteTimeSpan,token):Task.CompletedTask,
                DrainHotKeys=()=>{var batch=Events.ToArray();Events.Clear();return batch;}
            };
            Window=new(Paths,true,platform);Window.Show();Dispatcher.UIThread.RunJobs();
            Set(Window,"_hotkeysReady",true);
        }
        public ClickProfile Snapshot()=>(ClickProfile)Invoke(Window,"Snapshot")!;
        public void UseProfile(ClickProfile profile)=>Invoke(Window,"ApplyProfile",profile,null,true);
        public Task Execution=>Get<Task>(Window,"_executionTask");
        public void Start(ClickProfile profile,ExecutionRunMode mode)
        {
            UseProfile(profile);
            var plan=ExecutionPlanService.Create(profile,Display.Bounds);
            Invoke(Window,"LaunchExecution",profile,plan,ExecutionCheckpoint.Start,new[]{Display},mode);
        }
        public void Click(string title)
        {
            Window.UpdateLayout();
            var button=Window.GetVisualDescendants().OfType<Button>().First(b=>b.Content?.ToString()==title);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
        }
        public void Dispose()
        {
            Invoke(Window,"Stop","测试清理");
            var execution=Get<Task?>(Window,"_executionTask");
            if(execution!=null)Wait(execution);
            Invoke(Window,"EndCapture");Window.Close();Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class FakeMouse:IMouseController
    {
        private int _moves,_clicks,_ups,_x,_y;
        public int Moves=>Volatile.Read(ref _moves);
        public int Clicks=>Volatile.Read(ref _clicks);
        public int Ups=>Volatile.Read(ref _ups);
        public Action? AfterClick;
        public void MoveTo(int x,int y){_x=x;_y=y;Interlocked.Increment(ref _moves);}
        public bool IsAtPosition(int x,int y)=>_x==x&&_y==y;
        public void LeftClick(){Interlocked.Increment(ref _clicks);AfterClick?.Invoke();}
        public void EnsureLeftButtonUp()=>Interlocked.Increment(ref _ups);
    }
}
