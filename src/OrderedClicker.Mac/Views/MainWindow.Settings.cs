using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Theming;
using OrderedClicker.Platform;

namespace OrderedClicker.Mac.Views;
public sealed partial class MainWindow
{
    private static string CornerName(SafetyCorner corner)=>corner switch{SafetyCorner.TopLeft=>"左上角",SafetyCorner.TopRight=>"右上角",SafetyCorner.BottomLeft=>"左下角",_=>"右下角"};
    private async Task ShowSettings()
    {
        if(_state!=ExecutionState.Idle||_dialogOpen)return;
        EndCapture();_dialogOpen=true;
        var originalTheme=_settings.Theme;
        var dialog=new Window{Title="设置",Width=700,Height=740,MinHeight=620,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var content=new StackPanel{Margin=new(24),Spacing=16};
        content.Children.Add(TitleBlock("应用设置","主题、快捷键与安全停止设置会自动保存。"));
        var theme=new ComboBox{ItemsSource=AppThemeCatalog.All.Select(t=>t.DisplayName).ToArray(),SelectedIndex=(int)_settings.Theme,MinWidth=240};
        theme.SelectionChanged+=(_,_)=>{_settings.Theme=(AppThemeId)theme.SelectedIndex;ApplyTheme();};
        content.Children.Add(Card("外观",theme));
        var capture=new ShortcutEditor(_settings.CaptureHotKey);var start=new ShortcutEditor(_settings.StartPauseHotKey);var stop=new ShortcutEditor(_settings.StopHotKey);
        content.Children.Add(Card("全局快捷键",Row(Field("记录位置",capture),Field("开始 / 暂停",start),Field("停止",stop)),
            Row(Button("恢复 F6 / F7 / F8",()=>{capture.Set(HotKeyBindingService.DefaultCapture);start.Set(HotKeyBindingService.DefaultStartPause);stop.Set(HotKeyBindingService.DefaultStop);return Task.CompletedTask;}),
                Button("使用组合键",()=>{capture.Set(new(ShortcutKey.F6,ShortcutModifiers.Control|ShortcutModifiers.Alt));start.Set(new(ShortcutKey.F7,ShortcutModifiers.Control|ShortcutModifiers.Alt));stop.Set(new(ShortcutKey.F8,ShortcutModifiers.Control|ShortcutModifiers.Alt));return Task.CompletedTask;})),
            Muted("点击输入框后按组合键。单键仅允许 F6–F12；Mac 可能需要同时按 Fn。⌘ 表示 Command，⌥ 表示 Option。")));
        // Release registrations while editing so the OS can deliver the selected function keys to the input.
        _hotkeys?.RegisterInitial([]);_hotkeysReady=false;
        var enabled=new CheckBox{Content="启用安全角停止（运行必需）",IsChecked=_settings.SafetyCornerEnabled};
        var corner=new ComboBox{ItemsSource=new[]{"左上角","右上角","左下角","右下角"},SelectedIndex=(int)_settings.SafetyCorner,Width=140};
        var size=Number(_settings.SafetyCornerSize,4,64);var dwell=Number(_settings.SafetyCornerDwellMs,100,3000);
        content.Children.Add(Card("安全角",enabled,Row(Field("每个屏幕的角",corner),Field("范围 / 屏幕单位",size),Field("停留 / ms",dwell)),Muted("鼠标在任一显示器的所选角停留后，后台直接停止任务。点位不能与安全角重叠。")));
        var message=Text("",13);content.Children.Add(message);
        var saved=false;
        var save=Button("保存设置",()=>
        {
            if(!HotKeyBindingService.ValidateSet(capture.Binding,start.Binding,stop.Binding,out var error)){message.Text=error;return Task.CompletedTask;}
            var next=new AppSettings{Theme=(AppThemeId)theme.SelectedIndex,CaptureHotKey=capture.Binding,StartPauseHotKey=start.Binding,StopHotKey=stop.Binding,SafetyCornerEnabled=enabled.IsChecked==true,SafetyCorner=(SafetyCorner)corner.SelectedIndex,SafetyCornerSize=(int)size.Value!,SafetyCornerDwellMs=(int)dwell.Value!};
            var result=_hotkeys?.Replace(Registrations(next));
            if(result is {Success:false}){message.Text=result.Message;return Task.CompletedTask;}
            try{_settingsService.Save(next);_settings=next;_hotkeysReady=true;saved=true;dialog.Close();}
            catch(Exception e){message.Text=e.Message;}
            return Task.CompletedTask;
        },true);
        content.Children.Add(Row(save,Button("取消",()=>{dialog.Close();return Task.CompletedTask;})));
        dialog.Content=new ScrollViewer{Content=content};
        try{await dialog.ShowDialog(this);}
        finally
        {
            if(!saved)_settings.Theme=originalTheme;
            RegisterHotkeys();ApplyTheme();RefreshSummary();RefreshPermissions();_dialogOpen=false;
        }
    }
    private sealed class ShortcutEditor : TextBox
    {
        public HotKeyBinding Binding {get;private set;}
        public ShortcutEditor(HotKeyBinding binding)
        {
            Binding=binding;Width=180;IsReadOnly=true;Watermark="按下快捷键";Set(binding);
            KeyDown+=(_,e)=>
            {
                e.Handled=true;
                var name=e.Key switch{Key.Return=>"Enter",Key.Back=>"Back",Key.PageUp=>"PageUp",Key.PageDown=>"PageDown",_=>e.Key.ToString()};
                if(!Enum.TryParse<ShortcutKey>(name,out var key))return;
                if(key is ShortcutKey.LShiftKey or ShortcutKey.RShiftKey or ShortcutKey.LControlKey or ShortcutKey.RControlKey or ShortcutKey.LMenu or ShortcutKey.RMenu or ShortcutKey.LWin or ShortcutKey.RWin)return;
                var modifiers=ShortcutModifiers.None;
                if(e.KeyModifiers.HasFlag(KeyModifiers.Control))modifiers|=ShortcutModifiers.Control;
                if(e.KeyModifiers.HasFlag(KeyModifiers.Alt))modifiers|=ShortcutModifiers.Alt;
                if(e.KeyModifiers.HasFlag(KeyModifiers.Shift))modifiers|=ShortcutModifiers.Shift;
                if(e.KeyModifiers.HasFlag(KeyModifiers.Meta))modifiers|=ShortcutModifiers.Windows;
                Set(new(key,modifiers));
            };
        }
        public void Set(HotKeyBinding binding){Binding=binding;Text=KeyText(binding);}
    }
}
