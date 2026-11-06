using Avalonia.Controls;
using Avalonia.Threading;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;
using OrderedClicker.Mac.Platform;

namespace OrderedClicker.Mac.Views;
public sealed partial class MainWindow
{
    private IMouseController? _runningMouse;
    private IReadOnlyList<MonitorSnapshot>? _runningDisplays;
    private void RefreshPermissions()
    {
        try{_permission.Text=$"辅助功能：{(_platform.Accessibility()?"已允许":"未授权")}    屏幕录制：{(_platform.ScreenPermission()?"已允许":"未授权")}\n全局快捷键：{(_hotkeysReady?"已注册":"请在设置中检查或修改")}";}
        catch(Exception e){Error(e);}
    }
    private void RegisterHotkeys()
    {
        var result=_hotkeys!.RegisterInitial(Registrations(_settings));_hotkeysReady=result.Success;
        if(!result.Success)_status.Text=result.Message;
        UpdateShortcutText();
    }
    private static HotKeyRegistration[] Registrations(AppSettings settings)=>[
        new(1,"采点",settings.CaptureHotKey),new(2,"开始/暂停",settings.StartPauseHotKey),new(3,"停止",settings.StopHotKey)];
    private void Tick()
    {
        try
        {
            var events=_platform.PollEvents();
            var keys=_platform.DrainHotKeys();
            if((events&56)!=0)
            {
                EndCapture();Stop((events&32)!=0?"快捷键事件队列溢出，已停止。":"显示器或会话状态变化，请检查后重新开始");
            }
            else foreach(var key in keys)
            {
                if(key.Sequence<=_lastKeySequence)continue;
                _lastKeySequence=key.Sequence;
                if(key.Id==3)
                {
                    if(_captureMode!=CaptureMode.Idle||_bindingCapture)EndCapture();
                    else Stop("已通过快捷键停止");
                    break;
                }
                if(_dialogOpen)continue;
                if(key.Id==1&&_captureMode!=CaptureMode.Idle)
                {
                    if(key.PositionValid!=1){EndCapture();throw new IOException("无法读取按键时的鼠标位置，采点已取消。");}
                    CaptureAt(_platform.CaptureAt(key.X,key.Y));
                }
                if(key.Id==2)_=SafeStartPause();
            }
            if(_latest.TryConsume(out var progress)&&progress!=null)
            {
                _bar.Value=progress.PlannedClickCount==0?0:100d*progress.CompletedClickCount/progress.PlannedClickCount;
                _progressText.Text=$"第 {progress.CurrentLoop} / {progress.TotalLoops} 轮 · 点位 {progress.SourceIndex+1}\n{progress.CompletedClickCount:N0} / {progress.PlannedClickCount:N0} 次点击";
                if(progress.RequiresUserContinue)
                {
                    SetState(ExecutionState.Paused);_status.Text=progress.Message;
                    Show();Activate();
                }
                if(_hudText!=null)_hudText.Text=_progressText.Text+"\n"+_status.Text;
            }
        }
        catch(Exception e){if(_state!=ExecutionState.Idle)Stop("系统状态读取失败");Error(e);}
    }
    private async Task SafeStartPause(){try{await StartPause();}catch(Exception e){Error(e);}}
    private async Task CheckPlan(bool start)
    {
        if(_state!=ExecutionState.Idle||_dialogOpen)return;
        EndCapture();CommitGrid();var profile=Snapshot();var displays=_platform.Displays();
        var errors=MacPlanValidation.Validate(profile,_settings,displays);
        if(!_platform.Accessibility())errors=errors.Append("请先授权辅助功能。").ToArray();
        if(SelectedRunMode!=ExecutionRunMode.Rehearsal&&profile.ScreenStability.Enabled&&!_platform.ScreenPermission())errors=errors.Append("画面稳定检测需要屏幕录制权限。").ToArray();
        if(!_hotkeysReady)errors=errors.Append("请在设置中确保三个全局快捷键都注册成功。").ToArray();
        if(errors.Count>0){await Choose("检查未通过",string.Join("\n",errors.Take(15)),"返回修改");return;}
        var plan=ExecutionPlanService.Create(profile,MacNative.VirtualBounds(displays));
        RefreshSummary();
        var details=_summary.Text+"\n\n"+string.Join("\n",plan.Points.Take(12).Select(p=>$"{p.SourceIndex+1}. ({p.X}, {p.Y}) × {p.ClickCount} · 间隔 {p.ClickIntervalMs} ms · 点后等待 {p.AfterDelayMs} ms"));
        if(plan.Points.Count>12)details+=$"\n…共 {plan.Points.Count} 个启用点位";
        if(_recovery!=null&&SelectedRunMode!=ExecutionRunMode.Rehearsal)details+="\n\n从头开始会替换已有执行断点。";
        var result=await Choose("确认执行计划",details!,start?"开始执行":"确认并前往运行","返回修改");
        if(result!=0)return;
        ShowPage(4);
        if(start)LaunchExecution(profile,plan,ExecutionCheckpoint.Start,displays,SelectedRunMode);
        else _status.Text="执行计划已检查，点击开始执行后倒计时 3 秒。";
    }
    private async Task StartPause()
    {
        if(_dialogOpen||_closing)return;
        if(_state==ExecutionState.Running){_pause.Pause();SetState(ExecutionState.Paused);_status.Text="已暂停，可继续或停止。";Show();Activate();return;}
        if(_state==ExecutionState.Paused)
        {
            await ResumePaused();return;
        }
        if(_state==ExecutionState.Idle)await CheckPlan(true);
    }
    private async Task ResumeRecovery()
    {
        if(_recovery==null||_state!=ExecutionState.Idle||_dialogOpen)return;
        var recovery=_recovery;var profile=Snapshot();var displays=_platform.Displays();
        if(recovery.PlanFingerprint!=ExecutionPlanFingerprintService.Create(profile))throw new InvalidOperationException("当前方案与断点不一致。请先恢复对应草稿或打开对应方案。");
        var errors=MacPlanValidation.Validate(profile,_settings,displays);
        if(recovery.Plan.VirtualScreen!=MacNative.VirtualBounds(displays))errors=errors.Append("桌面布局与断点不一致，请重新检查并从头运行。").ToArray();
        if(!_platform.Accessibility()||!_hotkeysReady)errors=errors.Append("请检查辅助功能权限和全局快捷键。").ToArray();
        if(profile.ScreenStability.Enabled&&!_platform.ScreenPermission())errors=errors.Append("请授权屏幕录制。").ToArray();
        if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
        var answer=await Choose("从断点继续",$"已记录 {recovery.Checkpoint.CompletedClickCount} 次点击。\n异常退出时，最后一小段操作可能尚未保存，请先确认目标页面状态。\n\n继续前会重新定位到未完成点位。","确认继续","取消");
        if(answer==0)
        {
            EndCapture();
            if(SelectedRunMode==ExecutionRunMode.Rehearsal)_runMode.SelectedIndex=0;
            LaunchExecution(profile,recovery.Plan,recovery.Checkpoint,displays,SelectedRunMode);
        }
    }
    private ExecutionRunMode SelectedRunMode=>(ExecutionRunMode)Math.Max(0,_runMode.SelectedIndex);
    private void LaunchExecution(ClickProfile profile,ExecutionPlan plan,ExecutionCheckpoint checkpoint,IReadOnlyList<MonitorSnapshot> displays,ExecutionRunMode mode=ExecutionRunMode.Normal)
    {
        _activeRunMode=mode;_runningDisplays=displays;
        _pause=new();_executionCancellation=new();_lastStopReason=null;_latest.Clear();
        _bar.Value=plan.PlannedClickCount==0?0:100d*checkpoint.CompletedClickCount/plan.PlannedClickCount;
        SetState(ExecutionState.Countdown);ShowPage(4);
        _executionTask=RunExecution(profile,plan,checkpoint,displays,mode,_executionCancellation.Token);
    }
    private async Task RunExecution(ClickProfile profile,ExecutionPlan plan,ExecutionCheckpoint checkpoint,IReadOnlyList<MonitorSnapshot> displays,ExecutionRunMode mode,CancellationToken token)
    {
        var log=new ExecutionLogService(_paths);
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        using var sampler=new MacScreenSampler();
        using var watcherCancel=CancellationTokenSource.CreateLinkedTokenSource(token);
        var watcher=WatchSafety(displays,watcherCancel.Token);
        var lastSaved=DateTimeOffset.MinValue;
        var currentCheckpoint=checkpoint;
        var rehearsal=mode==ExecutionRunMode.Rehearsal;
        void Persist(ExecutionCheckpoint value)
        {
            if(!rehearsal)_checkpoints.Save(new(1,BuildVersion+"-mac",plan,plan.ProfileFingerprint,value,DateTimeOffset.UtcNow));
            lastSaved=DateTimeOffset.UtcNow;
        }
        void Remember(ExecutionCheckpoint value)
        {
            if(!rehearsal)_recovery=new(1,BuildVersion+"-mac",plan,plan.ProfileFingerprint,value,DateTimeOffset.UtcNow);
        }
        try
        {
            if(!rehearsal){_drafts.Save(new(profile,_sourcePath,_sourceTime,DateTime.UtcNow,_sourceHash));_lastDraft=SerializeCurrent();}
            Persist(checkpoint);
            await InputCountdown(token);
            token.ThrowIfCancellationRequested();SetState(ExecutionState.Running);
            _status.Text="执行中，按开始/暂停键暂停，按停止键结束。";
            var progress=new DirectProgress(p=>
            {
                currentCheckpoint=new(p.CurrentLoop-1,p.PointIndex-1,p.ClickIndex,p.Stage,p.CompletedPointExecutionCount,p.CompletedClickCount);
                if(DateTimeOffset.UtcNow-lastSaved>=TimeSpan.FromMilliseconds(500)||p.RequiresUserContinue)Persist(currentCheckpoint);
                _latest.Report(p);
            });
            _runningMouse=_platform.CreateMouse();
            var engine=new ClickExecutionEngine(_runningMouse,stabilityDetector:new ScreenStabilityDetector(sampler));
            var result=await Task.Run(async()=>
            {
                return await engine.ExecuteAsync(plan,checkpoint,_pause,progress,token,
                    new ExecutionRunOptions{Mode=mode,SafetyCheck=point=>CheckInputSafety(plan,displays,point)});
            });
            _latest.Clear();currentCheckpoint=result.NextCheckpoint;
            if(result.Outcome==ExecutionOutcome.Completed){if(!rehearsal){_checkpoints.Clear();_recovery=null;}_bar.Value=100;}
            else{Persist(currentCheckpoint);Remember(currentCheckpoint);}
            log.Write(plan,result);
            _runText.Text=result.Outcome switch{ExecutionOutcome.Completed=>"执行完成",ExecutionOutcome.Stopped=>"已停止",_=>"执行中断"};
            _progressText.Text=$"{result.CompletedClickCount:N0} / {result.PlannedClickCount:N0} 次点击 · 用时 {result.Elapsed.TotalSeconds:N1} 秒";
            _status.Text=_lastStopReason??result.Message;
        }
        catch(OperationCanceledException)
        {
            Persist(currentCheckpoint);Remember(currentCheckpoint);
            _runText.Text="已取消";_status.Text=_lastStopReason??"已取消执行。";
            log.Write(plan,new(ExecutionOutcome.Stopped,plan.PlannedPointExecutionCount,currentCheckpoint.CompletedPointExecutionCount,plan.PlannedClickCount,currentCheckpoint.CompletedClickCount,null,currentCheckpoint,_status.Text,elapsed.Elapsed));
        }
        catch(Exception e)
        {
            _runText.Text="执行中断";
            try{Persist(currentCheckpoint);Remember(currentCheckpoint);}catch(Exception saveError){Error(saveError);}
            Error(e);
        }
        finally
        {
            watcherCancel.Cancel();try{await watcher;}catch(OperationCanceledException){}
            _runningMouse=null;SetState(ExecutionState.Idle);CloseHud();_executionCancellation?.Dispose();_executionCancellation=null;
            if(!_closing){Show();Activate();}
            if(rehearsal)_lastDraft=SerializeCurrent();
            else try{SaveDraft();}catch(Exception error){Error(error);}
            _activeRunMode=ExecutionRunMode.Normal;
        }
    }
    private async Task InputCountdown(CancellationToken token)
    {
        ShowHud("执行状态","准备开始");Hide();
        for(var remaining=3;remaining>0;remaining--)
        {
            token.ThrowIfCancellationRequested();
            _runText.Text=$"{remaining} 秒后开始";_status.Text="请切换到目标应用。可按停止键取消。";
            if(_hudText!=null)_hudText.Text=_runText.Text+"\n"+_status.Text;
            await _platform.CountdownDelay(TimeSpan.FromSeconds(1),token);
        }
        token.ThrowIfCancellationRequested();CloseHud();Hide();
        if(!_platform.Accessibility())throw new InvalidOperationException("辅助功能权限失效。");
        if(_runningDisplays==null||!_platform.Displays().SequenceEqual(_runningDisplays))
            throw new InvalidOperationException("显示器布局或缩放变化，请重新检查。");
    }
    private async Task ResumePaused()
    {
        if(_executionCancellation==null||_runningDisplays==null)return;
        if(!_platform.Accessibility()||!_platform.Displays().SequenceEqual(_runningDisplays))
        { _status.Text="辅助功能权限或显示器状态变化，请检查后重新开始。"; return; }
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(_executionCancellation.Token);
        _resumeCancellation=cancellation;SetState(ExecutionState.Countdown);
        try
        {
            await InputCountdown(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _pause.Resume();SetState(ExecutionState.Running);_status.Text="继续执行。";
        }
        catch(OperationCanceledException){}
        catch(Exception e){SetState(ExecutionState.Paused);Show();Activate();Error(e);}
        finally{if(ReferenceEquals(_resumeCancellation,cancellation))_resumeCancellation=null;}
    }
    private string? CheckInputSafety(ExecutionPlan plan,IReadOnlyList<MonitorSnapshot> displays,ExecutionPlanPoint point)
    {
        try
        {
            if(!_platform.Accessibility())return "辅助功能权限失效，请重新授权。";
            if(!_platform.Displays().SequenceEqual(displays))return "显示器布局或缩放变化，请重新检查。";
            if(_inputUiVisible||_dialogOpen)return "请隐藏有序连点器主窗口后继续。";
            var own=_platform.OwnUiAt(point.X,point.Y);
            if(own!=0)return own>0?"点击位置被有序连点器窗口遮挡。":"无法确认点击位置的窗口，已暂停。";
            return MacTargetWindowGuard.Check(plan.TargetWindow,plan.TargetWindow==null?null:_platform.ProbeWindow(point.X,point.Y));
        }
        catch(Exception e){return "无法确认输入安全，已暂停："+e.Message;}
    }
    private async Task WatchSafety(IReadOnlyList<MonitorSnapshot> displays,CancellationToken token)
    {
        await Task.Run(async()=>
        {
            var corners=displays.Select(_=>new SafetyCornerService()).ToArray();var tick=0;var last=DateTimeOffset.UtcNow;
            while(!token.IsCancellationRequested)
            {
                try
                {
                    var now=DateTimeOffset.UtcNow;
                    if(now-last>TimeSpan.FromSeconds(3)){RequestBackgroundStop("系统休眠或调度中断，请检查目标页面后恢复。");return;}
                    last=now;var cursor=_platform.Cursor();
                    for(var i=0;i<displays.Count;i++)
                        if(corners[i].Update(cursor,displays[i].Bounds,_settings.SafetyCorner,_settings.SafetyCornerSize,TimeSpan.FromMilliseconds(_settings.SafetyCornerDwellMs),now))
                        {RequestBackgroundStop("安全角已触发停止。");return;}
                    if(++tick%10==0)
                    {
                        if(!_platform.Accessibility()){RequestBackgroundStop("辅助功能权限已撤销。");return;}
                        if(!_platform.Displays().SequenceEqual(displays)){RequestBackgroundStop("显示器布局或缩放变化，已停止。");return;}
                    }
                    await Task.Delay(50,token);
                }
                catch(OperationCanceledException){return;}
                catch(Exception e){RequestBackgroundStop("安全监控异常："+e.Message);return;}
            }
        });
    }
    private void RequestBackgroundStop(string reason)
    {
        Interlocked.Exchange(ref _lastStopReason,reason);_executionCancellation?.Cancel();_pause.Resume();
    }
    private void Stop(string reason)
    {
        _captureDelay.Cancel();
        if(_state==ExecutionState.Idle)return;
        _lastStopReason=reason;_resumeCancellation?.Cancel();_executionCancellation?.Cancel();_pause.Resume();SetState(ExecutionState.Stopping);
    }
    private void SetState(ExecutionState state)
    {
        _state=state;var idle=state==ExecutionState.Idle;
        foreach(var control in _editable)control.IsEnabled=idle;
        _settingsButton.IsEnabled=idle;_resume.IsEnabled=idle&&_recovery!=null;
        _start.IsEnabled=state is ExecutionState.Idle or ExecutionState.Running or ExecutionState.Paused;
        _start.Content=state switch{ExecutionState.Running=>"暂停",ExecutionState.Paused when _activeRunMode==ExecutionRunMode.Step=>"执行下一点",ExecutionState.Paused=>"继续",_=>"开始执行"};
        _stop.IsEnabled=!idle;
        if(state==ExecutionState.Running)_runText.Text="正在执行";
        if(state==ExecutionState.Paused)_runText.Text="已暂停";
        if(state==ExecutionState.Stopping)_runText.Text="正在安全停止…";
    }
    private async void OnClosing(object? sender,WindowClosingEventArgs e)
    {
        if(_allowClose)return;e.Cancel=true;if(_closing)return;
        _closing=true;_captureDelay.Cancel();_captureMode=CaptureMode.Idle;
        try
        {
            Stop("退出前安全停止");if(_executionTask!=null)await _executionTask;
            SaveDraft();_uiTimer.Stop();_draftTimer.Stop();_hotkeys?.Dispose();_captureDelay.Dispose();CloseHud();
            _allowClose=true;Close();
        }
        catch(Exception error){_closing=false;Error(error);}
    }
    private sealed class DirectProgress(Action<ExecutionProgress> report):IProgress<ExecutionProgress>{public void Report(ExecutionProgress value)=>report(value);}
}
