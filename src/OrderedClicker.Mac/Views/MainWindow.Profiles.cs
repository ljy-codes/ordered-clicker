using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;
using OrderedClicker.Mac.Platform;

namespace OrderedClicker.Mac.Views;
public sealed partial class MainWindow
{
    private void BindChanges()
    {
        _name.TextChanged+=(_,_)=>Changed();
        _mode.SelectionChanged+=(_,_)=>{if(!_loading){EndCapture();_profile.CoordinateMode=(CoordinateMode)_mode.SelectedIndex;Changed();}};
        foreach(var number in new[]{_loops,_loopDelay,_defaultInterval,_defaultAfter,_sample,_stableFor,_timeout,_tolerance}) number.ValueChanged+=(_,_)=>Changed();
        _stability.IsCheckedChanged+=(_,_)=>Changed();
        _grid.CellEditEnded+=(_,_)=>Changed();
    }
    private ClickProfile Snapshot()
    {
        var profile=ProfileService.CloneProfile(_profile);
        profile.Name=string.IsNullOrWhiteSpace(_name.Text)?"默认方案":_name.Text.Trim();
        profile.TotalLoops=(int)(_loops.Value??1);profile.LoopDelayMs=(int)(_loopDelay.Value??1000);
        profile.DefaultClickIntervalMs=(int)(_defaultInterval.Value??100);profile.DefaultAfterDelayMs=(int)(_defaultAfter.Value??500);
        profile.CoordinateMode=(CoordinateMode)_mode.SelectedIndex;
        profile.ScreenStability=new(){Enabled=_stability.IsChecked==true,SampleIntervalMs=(int)(_sample.Value??250),StableDurationMs=(int)(_stableFor.Value??750),TimeoutMs=(int)(_timeout.Value??15000),DifferenceTolerance=(double)(_tolerance.Value??0.02m)};
        profile.Points=_rows.Select(r=>r.Point).ToList();
        return ProfileService.CloneProfile(profile);
    }
    private string SerializeCurrent()=>JsonSerializer.Serialize(Snapshot());
    private bool Dirty=>_baseline!=SerializeCurrent();
    private void Changed()
    {
        if(_loading)return;
        _resume.IsEnabled=_recovery!=null&&_state==ExecutionState.Idle;
        Title="有序连点器 · macOS"+(Dirty?"  • 未保存":"");
        RefreshSummary();
    }
    private void RefreshSummary()
    {
        if(_loading)return;
        try
        {
            var p=Snapshot(); var enabled=p.Points.Where(x=>x.Enabled).ToArray();
            var duration=ExecutionDurationEstimator.Estimate(p);
            _summary.Text=$"{p.Name}\n启用 {enabled.Length} / {p.Points.Count} 个点位 · {p.TotalLoops} 轮循环\n计划点击 {enabled.Sum(x=>(long)x.ClickCount)*p.TotalLoops:N0} 次\n基础耗时 {duration.Base.TotalSeconds:N1} 秒"+
                (p.ScreenStability.Enabled?$" · 含稳定等待最多 {duration.Maximum.TotalSeconds:N1} 秒":"")+
                $"\n安全角：{CornerName(_settings.SafetyCorner)}，每个屏幕角停留 {_settings.SafetyCornerDwellMs} ms";
        }
        catch(Exception e){_summary.Text="请检查点位参数："+e.Message;}
    }
    private void RefreshRows()
    {
        _loading=true;
        var points=_rows.Select(r=>r.Point).ToArray(); var selected=_grid.SelectedIndex;
        _rows.Clear();for(var i=0;i<points.Length;i++)_rows.Add(new(points[i],i+1,Changed));
        _grid.SelectedIndex=Math.Clamp(selected,-1,_rows.Count-1);_loading=false;
    }
    private void ApplyProfile(ClickProfile profile,string? path,bool baseline)
    {
        _loading=true; _profile=profile;
        _name.Text=profile.Name;_mode.SelectedIndex=(int)profile.CoordinateMode;
        _loops.Value=profile.TotalLoops;_loopDelay.Value=profile.LoopDelayMs;
        _defaultInterval.Value=profile.DefaultClickIntervalMs;_defaultAfter.Value=profile.DefaultAfterDelayMs;
        _stability.IsChecked=profile.ScreenStability.Enabled;_sample.Value=profile.ScreenStability.SampleIntervalMs;
        _stableFor.Value=profile.ScreenStability.StableDurationMs;_timeout.Value=profile.ScreenStability.TimeoutMs;
        _tolerance.Value=(decimal)profile.ScreenStability.DifferenceTolerance;
        _rows.Clear();for(var i=0;i<profile.Points.Count;i++)_rows.Add(new(profile.Points[i],i+1,Changed));
        _sourcePath=path; _sourceHash=path!=null&&File.Exists(path)?ProfileService.ComputeFileSha256(path):null;
        _sourceTime=path!=null&&File.Exists(path)?File.GetLastWriteTimeUtc(path):null;
        _loading=false;if(baseline)_baseline=SerializeCurrent();
        RefreshRegion();RefreshLocal();Changed();
    }
    private void RefreshLocal()=>_localProfiles.ItemsSource=_profiles.ListLocalProfiles();
    private void SaveDraft()
    {
        if(_loading||_preview)return;
        var json=SerializeCurrent();
        if(json==_lastDraft)return;
        if(json==_baseline && _state==ExecutionState.Idle && _recovery==null) { _drafts.Discard();_lastDraft=json;return; }
        _drafts.Save(new(Snapshot(),_sourcePath,_sourceTime,DateTime.UtcNow,_sourceHash));_lastDraft=json;
    }
    private void RestoreDraftAndCheckpoint()
    {
        try
        {
            var draft=_drafts.Load();
            if(draft!=null)
            {
                var error=ProfileService.ValidateProfile(draft.Profile);
                if(error!=null)throw new InvalidDataException(error);
                ApplyProfile(draft.Profile,draft.SourcePath,false);
                _sourceHash=draft.SourceSha256;_sourceTime=draft.SourceUpdatedAtUtc;
                _status.Text="已恢复上次草稿，请检查后保存。";
                if(DraftService.HasSourceChanged(draft)){_sourcePath=null;_sourceHash=null;_sourceTime=null;_status.Text="已恢复草稿，来源方案已变化，请另存为。";}
            }
        }
        catch(Exception e){_drafts.QuarantineBrokenDraft();Error(new InvalidDataException("损坏草稿已隔离："+e.Message));}
        try
        {
            _recovery=_checkpoints.Load();
            _resume.IsEnabled=_recovery!=null;
            if(_recovery!=null){_runText.Text="发现未完成任务";_progressText.Text=$"已记录 {_recovery.Checkpoint.CompletedClickCount:N0} 次点击，可确认后从断点继续。";}
        }
        catch(Exception e){_checkpoints.QuarantineBrokenCheckpoint();Error(new InvalidDataException("损坏执行断点已隔离："+e.Message));}
    }
    private async Task<bool> ResolveUnsaved()
    {
        if(!Dirty)return true;
        var answer=await Choose("未保存的修改","切换方案前如何处理当前修改？", "保存","放弃修改","取消");
        if(answer==0){await SaveProfile(false);return !Dirty;}
        return answer==1;
    }
    private async Task NewProfile()
    {
        if(_state!=ExecutionState.Idle||!await ResolveUnsaved())return;
        EndCapture();ApplyProfile(new(),null,true);_drafts.Discard();_lastDraft="";_status.Text="已创建新方案。";
    }
    private async Task OpenLocal()
    {
        if(_localProfiles.SelectedItem is LocalProfileEntry entry)await LoadProfile(entry.Path);
    }
    private async Task OpenProfile()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="打开方案",AllowMultiple=false,FileTypeFilter=[new("有序连点器方案"){Patterns=["*.oclick"]}]});
        var path=files.FirstOrDefault()?.TryGetLocalPath();if(path!=null)await LoadProfile(path);
    }
    private async Task LoadProfile(string path)
    {
        if(_state!=ExecutionState.Idle||!await ResolveUnsaved())return;
        var result=_profiles.Load(path);if(!result.Success)throw new InvalidDataException(result.ErrorMessage);
        EndCapture();ApplyProfile(result.Profile!,path,true);_drafts.Discard();_lastDraft="";
        _status.Text="方案已打开。来自其他电脑的点位需要重新采集或校准。";
    }
    private async Task SaveProfile(bool saveAs)
    {
        if(_state!=ExecutionState.Idle)return;
        CommitGrid();var profile=Snapshot();
        var path=_sourcePath;
        if(saveAs)
        {
            var folder=await StorageProvider.TryGetFolderFromPathAsync(_paths.Profiles);
            var file=await StorageProvider.SaveFilePickerAsync(new(){Title="另存方案",SuggestedFileName=profile.Name+".oclick",DefaultExtension="oclick",ShowOverwritePrompt=true,SuggestedStartLocation=folder,FileTypeChoices=[new("有序连点器方案"){Patterns=["*.oclick"]}]});
            path=file?.TryGetLocalPath();if(path==null)return;
            if(_sourcePath!=null&&ProfileService.PathsEqual(path,_sourcePath))throw new IOException("另存为请选择不同文件，覆盖当前方案请使用保存。");
        }
        if(path==null)path=_profiles.GetAvailableProfilePath(profile.Name,profile.ProfileId);
        var hash=saveAs&&File.Exists(path)?ProfileService.ComputeFileSha256(path):_sourceHash;
        _profiles.Save(profile,path,new(CreateBackup:File.Exists(path),AllowOverwrite:File.Exists(path),RenewIdentity:saveAs,ExpectedExistingSha256:hash));
        ApplyProfile(profile,path,true);_drafts.Discard();_lastDraft=SerializeCurrent();_status.Text="方案已保存："+Path.GetFileName(path);
    }
    private async Task MigrateProfile()
    {
        if(!await ResolveUnsaved())return;
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="迁移旧方案",FileTypeFilter=[new("旧 JSON 方案"){Patterns=["*.json"]}]});
        var path=files.FirstOrDefault()?.TryGetLocalPath();if(path==null)return;
        var result=new LegacyProfileMigrationService().Migrate(path);
        if(!result.Success)throw new InvalidDataException(result.Error);
        ApplyProfile(result.Profile!,null,false);_status.Text="旧方案已迁移，请重新采点或校准并另存为。.json 原文件保留。 "+string.Join(" ",result.Warnings);
    }
    private Task ApplyAll(bool interval)
    {
        if(interval)PointTimingService.ApplyClickInterval(_rows.Select(r=>r.Point),(int)(_defaultInterval.Value??100));
        else PointTimingService.ApplyAfterDelay(_rows.Select(r=>r.Point),(int)(_defaultAfter.Value??500));
        RefreshRows();Changed();return Task.CompletedTask;
    }
    private Task MoveRow(int offset)
    {
        var index=_grid.SelectedIndex;var target=index+offset;
        if(index>=0&&target>=0&&target<_rows.Count){_rows.Move(index,target);RefreshRows();_grid.SelectedIndex=target;Changed();}return Task.CompletedTask;
    }
    private Task DeleteRow(){var i=_grid.SelectedIndex;if(i>=0){_rows.RemoveAt(i);RefreshRows();Changed();}return Task.CompletedTask;}
    private async Task ClearRows(){if(_rows.Count>0&&await Choose("清空点位","确认删除当前方案的全部点位？","清空","取消")==0){_rows.Clear();Changed();}}
    private void CommitGrid(){if(!_grid.CommitEdit(DataGridEditingUnit.Cell,true)||!_grid.CommitEdit(DataGridEditingUnit.Row,true))throw new InvalidOperationException("点位中存在未完成或无效的输入，请修正后重试。");}
    private async Task<int> Choose(string title,string message,params string[] choices)
    {
        _dialogOpen=true;
        var dialog=new Window{Title=title,Width=580,SizeToContent=SizeToContent.Height,MaxHeight=620,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var body=new StackPanel{Margin=new(26),Spacing=20};body.Children.Add(Text(title,22,true));body.Children.Add(new ScrollViewer{MaxHeight=400,Content=Text(message,15)});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,HorizontalAlignment=HorizontalAlignment.Right};
        for(var i=0;i<choices.Length;i++){var answer=i;var b=new Button{Content=choices[i]};if(i==0)b.Classes.Add("primary");b.Click+=(_,_)=>dialog.Close(answer);buttons.Children.Add(b);}
        body.Children.Add(buttons);dialog.Content=body;
        try{return await dialog.ShowDialog<int?>(this)??-1;}finally{_dialogOpen=false;}
    }
}
