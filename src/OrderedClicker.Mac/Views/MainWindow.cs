using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;
using OrderedClicker.Theming;
using OrderedClicker.Mac.Platform;

namespace OrderedClicker.Mac.Views;

public sealed partial class MainWindow : Window
{
    private readonly AppDataPaths _paths;
    private readonly bool _preview;
    private readonly ProfileService _profiles;
    private readonly DraftService _drafts;
    private readonly SettingsService _settingsService;
    private readonly ExecutionCheckpointService _checkpoints;
    private readonly DiagnosticLogService _diagnostics;
    private AppSettings _settings;
    private ClickProfile _profile = new();
    private string? _sourcePath, _sourceHash;
    private DateTime? _sourceTime;
    private string _baseline = "", _lastDraft = "";
    private bool _loading, _allowClose, _closing, _dialogOpen, _hotkeysReady;
    private readonly ObservableCollection<PointRow> _rows = [];
    private readonly TextBox _name = new() { Watermark="方案名称", MinWidth=220 };
    private readonly ComboBox _localProfiles = new() { MinWidth=220, PlaceholderText="选择本机方案" };
    private readonly ComboBox _mode = new() { ItemsSource=new[]{"本机坐标","云桌面相对坐标"}, SelectedIndex=0, MinWidth=240 };
    private readonly NumericUpDown _loops = Number(1,1,100000), _loopDelay = Number(1000,0,600000);
    private readonly NumericUpDown _defaultInterval = Number(100,10,600000), _defaultAfter = Number(500,0,600000);
    private readonly CheckBox _stability = new() { Content="每个点位完成后，等待画面稳定" };
    private readonly NumericUpDown _sample = Number(250,50,10000), _stableFor = Number(750,100,600000), _timeout = Number(15000,100,600000);
    private readonly NumericUpDown _tolerance = new() { Value=0.02m, Minimum=0, Maximum=1, Increment=0.01m, FormatString="0.00", Width=112 };
    private readonly DataGrid _grid = new() { AutoGenerateColumns=false, IsReadOnly=false, SelectionMode=DataGridSelectionMode.Single, MinHeight=220 };
    private readonly TextBlock _status = Text("就绪 · 先选择模式，再创建方案", 13);
    private readonly TextBlock _permission = Text("正在检查系统权限", 13);
    private readonly TextBlock _regionText = Text("尚未校准云桌面区域", 14);
    private readonly TextBlock _summary = Text("配置点位后，生成执行计划。", 15);
    private readonly TextBlock _runText = Text("等待开始", 24);
    private readonly TextBlock _progressText = Text("0 / 0 次点击", 15);
    private readonly TextBlock _shortcuts = Text("", 12);
    private readonly TextBlock _captureText = Text("先点击“开始采点”，再将鼠标移到目标位置按采点键。",14);
    private readonly ProgressBar _bar = new() { Minimum=0,Maximum=100,Height=8 };
    private readonly ContentControl _page = new();
    private readonly List<Control> _pages = [];
    private readonly List<Button> _nav = [];
    private readonly List<Control> _editable = [];
    private Button _start=null!, _stop=null!, _resume=null!, _captureButton=null!, _settingsButton=null!;
    private readonly DispatcherTimer _uiTimer = new() { Interval=TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _draftTimer = new() { Interval=TimeSpan.FromSeconds(2) };
    private HotKeyRegistrationCoordinator? _hotkeys;
    private CaptureMode _captureMode;
    private CapturedPoint? _firstCorner;
    private readonly ReplaceableDelay _captureDelay = new();
    private Window? _hud;
    private TextBlock? _hudText;
    private CancellationTokenSource? _executionCancellation;
    private Task? _executionTask;
    private AsyncPauseGate _pause = new();
    private readonly LatestExecutionProgress _latest = new();
    private ExecutionState _state;
    private ExecutionCheckpointEnvelope? _recovery;
    private string? _lastStopReason;
    private int _pageIndex;

    public MainWindow(AppDataPaths paths, bool preview=false)
    {
        _paths=paths; _preview=preview;
        _profiles=new(paths); _drafts=new(paths); _settingsService=new(paths); _checkpoints=new(paths); _diagnostics=new(paths);
        var loaded=_settingsService.LoadWithResult(); _settings=loaded.Settings;
        Title="有序连点器 · macOS"; Width=1180; Height=790; MinWidth=1000; MinHeight=660;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Icon=new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://OrderedClicker.Mac/Assets/ordered-clicker.ico")));
        BuildLayout(); ApplyTheme(); BindChanges(); ApplyProfile(new ClickProfile(),null,true); SetState(ExecutionState.Idle);
        if (loaded.Warning != null) _status.Text=loaded.Warning;
        Opened += async (_,_) =>
        {
            if (_preview) { if (Program.RenderDirectory != null) await RenderPreview(); return; }
            try
            {
                MacNative.oc_initialize(); _hotkeys=new(new MacHotKeyRegistrar());
                RegisterHotkeys(); RestoreDraftAndCheckpoint(); RefreshPermissions();
                if (_settings.RequiresSaveAfterLoad) _settingsService.Save(_settings);
                new LogRetentionService().Prune(_paths.Logs,LogRetentionPolicy.Default,DateTimeOffset.UtcNow);
                new LogRetentionService().Prune(_paths.Diagnostics,LogRetentionPolicy.Default,DateTimeOffset.UtcNow);
            }
            catch(Exception e) { Error(e); }
            _uiTimer.Start(); _draftTimer.Start();
        };
        _uiTimer.Tick += (_,_) => Tick();
        _draftTimer.Tick += (_,_) => { try { SaveDraft(); } catch(Exception e) { Error(e); } };
        Closing += OnClosing;
        Activated += (_,_) => { if (!_preview && IsVisible) RefreshPermissions(); };
        KeyDown += (_,e) => { if (e.Key==Avalonia.Input.Key.Escape && _captureMode!=CaptureMode.Idle) EndCapture(); };
    }

    private void BuildLayout()
    {
        var root=new Grid { RowDefinitions=new("Auto,*,Auto") };
        var header=new Grid { ColumnDefinitions=new("*,Auto"), Margin=new(26,20,26,16) };
        var branding=new StackPanel { Spacing=4 };
        branding.Children.Add(Text("有序连点器",25,true));
        branding.Children.Add(Muted("ORDERED CLICKER  /  macOS 2.1.0"));
        header.Children.Add(branding);
        _settingsButton=Button("设置", async()=>await ShowSettings());
        var actions=Row(Button("使用说明",()=>{ OpenGuide(); return Task.CompletedTask; }), _settingsButton);
        Grid.SetColumn(actions,1); header.Children.Add(actions); root.Children.Add(header);
        var workspace=new Grid { ColumnDefinitions=new("180,*"), Margin=new(20,0,20,14) };
        Grid.SetRow(workspace,1); root.Children.Add(workspace);
        var sidebar=new DockPanel { Margin=new(0,0,18,0) };
        var hints=new StackPanel { Spacing=10,Margin=new(8,20,4,8) };
        hints.Children.Add(Muted("全局快捷键")); hints.Children.Add(_shortcuts);
        hints.Children.Add(Muted("紧急停止\n停止键 + 屏幕安全角"));
        DockPanel.SetDock(hints,Dock.Bottom); sidebar.Children.Add(hints);
        var navigation=new StackPanel { Spacing=8 };
        var titles=new[]{"模式","方案","采点","检查","运行 / 日志"};
        for (var i=0;i<titles.Length;i++)
        {
            var index=i;
            var button=Button($"0{i+1}   {titles[i]}",()=>{ ShowPage(index); return Task.CompletedTask; });
            button.HorizontalContentAlignment=HorizontalAlignment.Left; button.HorizontalAlignment=HorizontalAlignment.Stretch;
            button.Height=49; _nav.Add(button); navigation.Children.Add(button);
        }
        sidebar.Children.Add(navigation); workspace.Children.Add(sidebar);
        Grid.SetColumn(_page,1); workspace.Children.Add(_page);
        _pages.Add(BuildMode()); _pages.Add(BuildProfile()); _pages.Add(BuildCapture()); _pages.Add(BuildCheck()); _pages.Add(BuildRun());
        var footer=new Border { Padding=new(26,12), BorderThickness=new(0,1,0,0), BorderBrush=Brushes.DimGray, Child=_status };
        Grid.SetRow(footer,2); root.Children.Add(footer); Content=root;
        ShowPage(0);
    }

    private Control BuildMode()
    {
        var page=Page("01 / 选择模式","本机操作或云桌面，按相同顺序执行。",out var content);
        content.Children.Add(Card("坐标模式", _mode, Muted("本机：记录屏幕位置。云桌面：记录校准区域内的相对位置。"),
            Row(Button("校准云桌面区域",()=>{ BeginCalibration(); return Task.CompletedTask; }), Button("预览区域",PreviewRegion)), _regionText));
        content.Children.Add(Card("系统权限",_permission,
            Row(Button("授权辅助功能",()=>{ MacNative.oc_accessibility(1); OpenPrivacy("Privacy_Accessibility"); return Task.CompletedTask; }),
                Button("授权屏幕录制",()=>{ MacNative.oc_screen_permission(1); OpenPrivacy("Privacy_ScreenCapture"); return Task.CompletedTask; }),
                Button("重新检查",()=>{ RefreshPermissions(); return Task.CompletedTask; })),
            Muted("辅助功能用于控制鼠标；屏幕录制仅在开启画面稳定检测时需要。授权后若仍未生效，请退出并重新打开应用。")));
        content.Children.Add(Button("下一步：创建方案 →",()=>{ ShowPage(1); return Task.CompletedTask; },true));
        _editable.Add(_mode); return page;
    }

    private Control BuildProfile()
    {
        var page=Page("02 / 管理方案","方案、草稿和日志保存在当前用户目录。",out var content);
        content.Children.Add(Card("当前方案",_name,
            Row(Button("新建",NewProfile),Button("保存",()=>SaveProfile(false),true),Button("另存为",()=>SaveProfile(true))),
            Row(_localProfiles,Button("打开选中",OpenLocal)),
            Row(Button("打开 .oclick",OpenProfile),Button("迁移旧 JSON",MigrateProfile),Button("方案目录",()=>OpenFolder(_paths.Profiles)))));
        content.Children.Add(Card("循环与默认时间", Row(Field("总循环次数",_loops),Field("轮间等待 / ms",_loopDelay)),
            Row(Field("点击间隔 / ms",_defaultInterval),Button("间隔应用全部",()=>ApplyAll(true)),
                Field("点后等待 / ms",_defaultAfter),Button("等待应用全部",()=>ApplyAll(false))),
            Muted("新点位使用默认时间；已有点位可在采点列表中单独修改。")));
        _editable.Add(content); return page;
    }

    private Control BuildCapture()
    {
        var page=new Grid { RowDefinitions=new("Auto,Auto,*,Auto"), RowSpacing=16 };
        page.Children.Add(TitleBlock("03 / 采集点位","按列表顺序执行，可调整顺序、启用状态和每点参数。"));
        _captureButton=Button("开始采点",()=>{ BeginCapture(); return Task.CompletedTask; },true);
        var toolbar=new StackPanel { Spacing=10 };
        toolbar.Children.Add(Row(_captureButton,Button("2 秒后记录",DelayedCapture),Button("结束 / 取消采点",()=>{ EndCapture(); return Task.CompletedTask; })));
        toolbar.Children.Add(_captureText); Grid.SetRow(toolbar,1); page.Children.Add(toolbar);
        _grid.ItemsSource=_rows;
        _grid.Columns.Add(new DataGridTextColumn { Header="#",Binding=new Binding("Number"),IsReadOnly=true,Width=new DataGridLength(45) });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header="启用",Binding=new Binding("Enabled"),Width=new DataGridLength(76) });
        foreach(var column in new[]{("X","X"),("Y","Y"),("点击次数","ClickCount"),("间隔 / ms","ClickIntervalMs"),("点后等待 / ms","AfterDelayMs")})
            _grid.Columns.Add(new DataGridTextColumn { Header=column.Item1,Binding=new Binding(column.Item2){Mode=BindingMode.TwoWay},Width=new DataGridLength(1,DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header="相对位置",Binding=new Binding("Relative"),IsReadOnly=true,Width=new DataGridLength(120) });
        Grid.SetRow(_grid,2); page.Children.Add(_grid);
        var bottom=Row(Button("上移",()=>MoveRow(-1)),Button("下移",()=>MoveRow(1)),Button("删除选中",DeleteRow),Button("清空点位",ClearRows),Button("下一步：检查 →",()=>{ ShowPage(3); return Task.CompletedTask; },true));
        Grid.SetRow(bottom,3); page.Children.Add(bottom);
        _editable.Add(toolbar);_editable.Add(_grid);_editable.Add(bottom);return page;
    }

    private Control BuildCheck()
    {
        var page=Page("04 / 检查执行计划","检查坐标、循环、预计耗时和安全停止方式。",out var content);
        content.Children.Add(Card("画面稳定检测",_stability,
            Row(Field("采样间隔 / ms",_sample),Field("稳定时长 / ms",_stableFor),Field("超时 / ms",_timeout),Field("变化容差 / 0–1",_tolerance)),
            Muted("超时自动暂停，确认目标页面可操作后继续。云桌面模式检测校准区域，本机模式检测桌面。")));
        content.Children.Add(Card("执行概览",_summary,Button("生成并确认执行计划",()=>CheckPlan(false),true)));
        _editable.Add(content);return page;
    }

    private Control BuildRun()
    {
        var page=Page("05 / 运行与日志","开始前有 3 秒倒计时，期间可切换到目标应用。",out var content);
        _start=Button("开始执行",StartPause,true); _stop=Button("停止",()=>{ Stop("已手动停止");return Task.CompletedTask; }); _stop.Classes.Add("danger");
        _resume=Button("从断点继续",ResumeRecovery);_resume.IsEnabled=false;
        content.Children.Add(Card("执行状态",_runText,_progressText,_bar,Row(_start,_stop,_resume)));
        content.Children.Add(Card("日志与数据",Row(Button("执行日志",()=>OpenFolder(_paths.Logs)),Button("诊断日志",()=>OpenFolder(_paths.Diagnostics)),Button("数据目录",()=>OpenFolder(_paths.Root))),
            Muted("每次运行记录结果、点击计数及停止位置。日志保留最多 30 天、1000 个文件、100 MB。")));
        return page;
    }

    internal void ShowPage(int index)
    {
        _pageIndex=index; _page.Content=_pages[index];
        for(var i=0;i<_nav.Count;i++) { if(i==index)_nav[i].Classes.Add("primary");else _nav[i].Classes.Remove("primary"); }
        if(index==3) RefreshSummary();
    }
    private void ApplyTheme()
    {
        var theme=AppThemeCatalog.Get(_settings.Theme);
        Application.Current!.RequestedThemeVariant=theme.IsDark?ThemeVariant.Dark:ThemeVariant.Light;
        Background=new SolidColorBrush(Color.FromRgb(theme.Window.R,theme.Window.G,theme.Window.B));
        Application.Current.Resources["CardBrush"]=new SolidColorBrush(Color.FromRgb(theme.Surface.R,theme.Surface.G,theme.Surface.B));
        Application.Current.Resources["AccentBrush"]=new SolidColorBrush(Color.FromRgb(theme.Primary.R,theme.Primary.G,theme.Primary.B));
        UpdateShortcutText();
    }
    private static string KeyText(HotKeyBinding binding)=>HotKeyBindingService.Format(binding).Replace("Win","⌘").Replace("Alt","⌥").Replace("Ctrl","⌃");
    private void UpdateShortcutText()=>_shortcuts.Text=$"{KeyText(_settings.CaptureHotKey)}  记录位置\n{KeyText(_settings.StartPauseHotKey)}  开始 / 暂停\n{KeyText(_settings.StopHotKey)}  停止";
    private static NumericUpDown Number(int value,int min,int max)=>new(){Value=value,Minimum=min,Maximum=max,Increment=1,FormatString="0",Width=130};
    private static TextBlock Text(string value,double size=14,bool bold=false)=>new(){Text=value,FontSize=size,FontWeight=bold?FontWeight.SemiBold:FontWeight.Normal,TextWrapping=TextWrapping.Wrap};
    private static TextBlock Muted(string value)=>new(){Text=value,FontSize=13,Opacity=0.68,TextWrapping=TextWrapping.Wrap,LineHeight=22};
    private static StackPanel Row(params Control[] items) { var panel=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,VerticalAlignment=VerticalAlignment.Center};foreach(var c in items)panel.Children.Add(c);return panel; }
    private static StackPanel Field(string label,Control value) { var panel=new StackPanel{Spacing=7};panel.Children.Add(Muted(label));panel.Children.Add(value);return panel; }
    private static StackPanel TitleBlock(string title,string description) { var p=new StackPanel{Spacing=7,Margin=new(0,0,0,8)};p.Children.Add(Text(title,25,true));p.Children.Add(Muted(description));return p; }
    private static ScrollViewer Page(string title,string description,out StackPanel content) {content=new StackPanel{Spacing=18};content.Children.Add(TitleBlock(title,description));return new ScrollViewer{Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};}
    private static Border Card(string title,params Control[] controls) { var p=new StackPanel{Spacing=14};p.Children.Add(Text(title,16,true));foreach(var c in controls)p.Children.Add(c);var b=new Border{Padding=new(22),CornerRadius=new(12),Child=p};b.Classes.Add("card");return b; }
    private Button Button(string title,Func<Task> action,bool primary=false) { var b=new Button{Content=title};if(primary)b.Classes.Add("primary");b.Click+=async(_,_)=>{try{await action();}catch(Exception e){Error(e);}};return b; }
    private void Error(Exception e) { _status.Text=e.Message;try{_diagnostics.Write("mac.ui",e);}catch{} }
    private static Task OpenFolder(string path) { Process.Start(new ProcessStartInfo("/usr/bin/open"){ArgumentList={path}});return Task.CompletedTask; }
    private static void OpenPrivacy(string pane) { Process.Start(new ProcessStartInfo("/usr/bin/open"){ArgumentList={"x-apple.systempreferences:com.apple.preference.security?"+pane}}); }
    private void OpenGuide()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Resources","使用说明.html"));
        if(!File.Exists(path)) path=Path.Combine(AppContext.BaseDirectory,"使用说明.html");
        if(!File.Exists(path)) path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","使用说明.html"));
        if(File.Exists(path)) _=OpenFolder(path);else _status.Text="请打开交付目录中的“产品说明.html”。";
    }
}
