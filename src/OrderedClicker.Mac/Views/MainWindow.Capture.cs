using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;
using OrderedClicker.Mac.Platform;

namespace OrderedClicker.Mac.Views;
public sealed partial class MainWindow
{
    private bool _bindingCapture;
    private void BeginCapture()
    {
        if(_state!=ExecutionState.Idle||_dialogOpen)return;
        if(_mode.SelectedIndex==1&&_profile.CloudDesktopRegion==null)throw new InvalidOperationException("请先在模式页校准云桌面区域。");
        CommitGrid();_captureDelay.Cancel();_bindingCapture=false;_captureMode=CaptureMode.PointCapture;_captureButton.Content="采点中";
        _captureText.Text=$"移动鼠标后按 {KeyText(_settings.CaptureHotKey)} 记录，按 {KeyText(_settings.StopHotKey)} 结束。";
        ShowHud("正在采点",_captureText.Text);Hide();
    }
    private void BeginCalibration()
    {
        if(_state!=ExecutionState.Idle||_dialogOpen)return;
        _captureDelay.Cancel();_bindingCapture=false;_captureMode=CaptureMode.CloudRegionTopLeft;_firstCorner=null;
        _status.Text=$"将鼠标移到云桌面左上角，按 {KeyText(_settings.CaptureHotKey)}。";
        ShowHud("校准云桌面",_status.Text);Hide();
    }
    private void CaptureAtCursor()
    {
        var p=_platform.Cursor();
        CaptureAt(_platform.CaptureAt(p.X,p.Y));
    }
    private void BeginStabilityRegion()
    {
        if(_state!=ExecutionState.Idle||_dialogOpen)return;
        _captureDelay.Cancel();_bindingCapture=false;_captureMode=CaptureMode.StabilityRegionTopLeft;_firstCorner=null;
        _status.Text=$"将鼠标移到检测区域左上角，按 {KeyText(_settings.CaptureHotKey)}。";
        ShowHud("检测区域",_status.Text);Hide();
    }
    private void CaptureAt(CapturedPoint p)
    {
        if(_state!=ExecutionState.Idle||_captureMode==CaptureMode.Idle)return;
        if(_captureMode is CaptureMode.CloudRegionTopLeft or CaptureMode.StabilityRegionTopLeft)
        {
            var stability=_captureMode==CaptureMode.StabilityRegionTopLeft;
            _firstCorner=p;_captureMode=stability?CaptureMode.StabilityRegionBottomRight:CaptureMode.CloudRegionBottomRight;
            _status.Text=$"左上角 ({p.X}, {p.Y}) 已记录。移到右下角按 {KeyText(_settings.CaptureHotKey)}。";
            if(_hudText!=null)_hudText.Text=_status.Text;return;
        }
        if(_captureMode==CaptureMode.StabilityRegionBottomRight)
        {
            var first=_firstCorner!;
            if(p.X<=first.X||p.Y<=first.Y)throw new InvalidOperationException("右下角必须位于左上角的右下方。");
            var bounds=new ScreenBounds(first.X,first.Y,p.X-first.X,p.Y-first.Y);
            if(!MacPlanValidation.IsRegionCovered(bounds,_platform.Displays()))
                throw new InvalidOperationException("检测区域不能包含显示器之外的空隙。");
            _profile.ScreenStability.Region=bounds;
            EndCapture();RefreshRegion();Changed();_status.Text="画面检测区域已更新。";return;
        }
        if(_captureMode==CaptureMode.CloudRegionBottomRight)
        {
            var first=_firstCorner!;
            if(first.MonitorDeviceName!=p.MonitorDeviceName)throw new InvalidOperationException("云桌面区域的两个角必须位于同一显示器。");
            var region=new CloudDesktopRegion{X=first.X,Y=first.Y,Width=p.X-first.X,Height=p.Y-first.Y,MonitorDeviceName=p.MonitorDeviceName,CapturedDpi=p.Dpi};
            CloudDesktopCoordinateService.ValidateRegion(region);
            _profile.CloudDesktopRegion=region;_mode.SelectedIndex=1;
            CloudDesktopCoordinateService.FillMissingRelativeCoordinates(_rows.Select(r=>r.Point),region);
            EndCapture();RefreshRegion();RefreshRows();Changed();_status.Text="云桌面区域已校准。";return;
        }
        var point=new ClickPoint{X=p.X,Y=p.Y,MonitorDeviceName=p.MonitorDeviceName,MonitorBounds=p.MonitorBounds,CapturedDpi=p.Dpi};
        PointTimingService.ApplyDefaults(point,(int)(_defaultInterval.Value??100),(int)(_defaultAfter.Value??500));
        if(_mode.SelectedIndex==1)
        {
            var relative=CloudDesktopCoordinateService.ToRelative(_profile.CloudDesktopRegion!,p.X,p.Y);
            point.RelativeX=relative.X;point.RelativeY=relative.Y;
        }
        _rows.Add(CreateRow(point,_rows.Count+1));Changed();
        _status.Text=$"已记录第 {_rows.Count} 个点位：({p.X}, {p.Y})";
        if(_hudText!=null)_hudText.Text=_status.Text+"\n"+_captureText.Text;
    }
    private async Task CaptureTargetWindow()
    {
        if(_state!=ExecutionState.Idle||_dialogOpen)return;
        EndCapture();_bindingCapture=true;
        ShowHud("绑定目标窗口","2 秒后读取鼠标所在窗口");Hide();
        await _captureDelay.RunAsync(TimeSpan.FromSeconds(2),async token=>
        {
            await Dispatcher.UIThread.InvokeAsync(()=>
            {
                if(token.IsCancellationRequested||!_bindingCapture)return;
                try
                {
                    if(!_platform.Accessibility())throw new InvalidOperationException("绑定窗口需要辅助功能权限。");
                    var p=_platform.Cursor();
                    var window=_platform.ProbeWindow(p.X,p.Y);
                    if(window==null)throw new InvalidOperationException("无法读取实际窗口，请切换目标应用后重新绑定。");
                    var binding=new TargetWindowBinding{Platform="macOS",ApplicationId=window.ApplicationId,WindowTitle=window.WindowTitle};
                    var error=MacTargetWindowGuard.Check(binding,window);
                    if(error!=null)throw new InvalidOperationException(error);
                    _profile.TargetWindow=binding;RefreshRegion();Changed();_status.Text="目标窗口已绑定。";
                }
                catch(Exception e){Error(e);}
                finally{EndCapture();}
            });
        });
    }
    private async Task DelayedCapture()
    {
        if(_state!=ExecutionState.Idle)return;
        if(_captureMode==CaptureMode.Idle)BeginCapture();
        if(_hudText!=null)_hudText.Text="2 秒后记录当前鼠标位置…";
        await _captureDelay.RunAsync(TimeSpan.FromSeconds(2),async token=>
        {
            await Dispatcher.UIThread.InvokeAsync(()=>{if(!token.IsCancellationRequested){try{CaptureAtCursor();}catch(Exception e){Error(e);}if(_captureMode==CaptureMode.PointCapture)EndCapture();}});
        });
    }
    private void EndCapture()
    {
        _captureDelay.Cancel();_bindingCapture=false;_captureMode=CaptureMode.Idle;_firstCorner=null;
        _captureButton.Content="开始采点";_captureText.Text="先点击“开始采点”，再将鼠标移到目标位置按采点键。";
        if(_state==ExecutionState.Idle)CloseHud();
        if(!IsVisible&&!_closing){Show();Activate();}
    }
    private void RefreshRegion()
    {
        var r=_profile.CloudDesktopRegion;
        _regionText.Text=r==null?"尚未校准云桌面区域":$"已校准：({r.X}, {r.Y}) · {r.Width} × {r.Height} 屏幕坐标单位";
        var stability=_profile.ScreenStability.Region;
        _stabilityRegionText.Text=stability is {} s?$"检测范围：({s.X}, {s.Y}) · {s.Width} × {s.Height}":"检测范围：自动";
        _targetWindowText.Text=_profile.TargetWindow is {} target?$"{target.ApplicationId} · {target.WindowTitle}":"未绑定目标窗口";
    }
    [System.Runtime.InteropServices.DllImport("OrderedClickerNative")]
    private static extern void oc_preview_region(int x,int y,int width,int height);
    private Task PreviewRegion()
    {
        if(_profile.CloudDesktopRegion is not {} r)throw new InvalidOperationException("请先校准区域。");
        oc_preview_region(r.X,r.Y,r.Width,r.Height);return Task.CompletedTask;
    }
    private void ShowHud(string title,string message)
    {
        CloseHud();_hudText=Text(message,13);
        var content=new StackPanel{Margin=new(16),Spacing=12};content.Children.Add(Text(title,17,true));content.Children.Add(_hudText);
        content.Children.Add(Row(Button("显示主窗口",()=>
        {
            if(_state==ExecutionState.Running){_pause.Pause();SetState(ExecutionState.Paused);}
            else if(_state==ExecutionState.Countdown)Stop("已取消倒计时并显示主窗口。");
            Show();Activate();return Task.CompletedTask;
        }),Button("停止 / 结束",()=>{if(_captureMode!=CaptureMode.Idle||_bindingCapture)EndCapture();else Stop("已手动停止");return Task.CompletedTask;})));
        var hud=new Window{Title=title,Width=390,SizeToContent=SizeToContent.Height,CanResize=false,Topmost=true,ShowInTaskbar=false,ShowActivated=false,Content=content};
        _hud=hud;
        hud.Closing+=(_,_)=>
        {
            if(!ReferenceEquals(_hud,hud))return;
            _hud=null;_hudText=null;
            if(_captureMode!=CaptureMode.Idle||_bindingCapture)EndCapture();
            else if(_state!=ExecutionState.Idle)Stop("状态窗口关闭，已停止执行");
        };
        hud.Show();
    }
    private void CloseHud(){var hud=_hud;_hud=null;_hudText=null;hud?.Close();}
}
