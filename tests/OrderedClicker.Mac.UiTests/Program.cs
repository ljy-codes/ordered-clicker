using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OrderedClicker.Mac;
using OrderedClicker.Mac.Views;
using OrderedClicker.Services;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
var root=Path.Combine(Path.GetTempPath(),"oc-ui-tests-"+Guid.NewGuid());
try
{
    var paths=AppDataPaths.Create(root,false);
    var window=new MainWindow(paths,true);
    window.Show();Dispatcher.UIThread.RunJobs();
    Button Find(string text)=>window.GetVisualDescendants().OfType<Button>().First(x=>x.Content?.ToString()==text);
    void Click(string text){Find(text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
    void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    foreach(var page in new[]{"01   模式","02   方案","03   采点","04   检查","05   运行 / 日志"})
    {
        Click(page);
        window.UpdateLayout();
        Assert(window.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text?.StartsWith(page[..2]+" /")==true),"页面导航未显示预期内容："+page);
    }
    Assert(!Find("停止").IsEnabled,"空闲时停止按钮必须禁用");
    Assert(!Find("从断点继续").IsEnabled,"不存在断点时不能继续");
    Click("02   方案");
    var name=window.GetVisualDescendants().OfType<TextBox>().First(x=>x.Watermark=="方案名称");
    name.Text="界面保存测试";Dispatcher.UIThread.RunJobs();
    Click("保存");
    var file=Directory.GetFiles(paths.Profiles,"*.oclick").Single();
    Assert(new ProfileService(paths).Load(file).Profile?.Name=="界面保存测试","保存按钮必须写出当前输入的方案");
    var numbers=window.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
    numbers[0].Value=7;Dispatcher.UIThread.RunJobs();Click("保存");
    Assert(new ProfileService(paths).Load(file).Profile?.TotalLoops==7,"循环输入必须保存到方案");
    File.WriteAllText(file,File.ReadAllText(file)+"\n");
    name.Text="发生冲突";Click("保存");
    Assert(new ProfileService(paths).Load(file).Profile?.Name=="界面保存测试","外部修改冲突不得静默覆盖");
    Assert(window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("修改")==true),"保存冲突必须显示提示");
    window.Close();Dispatcher.UIThread.RunJobs();
    Console.WriteLine("PASS 5-page navigation, idle safety controls, profile save, loop settings, external-write conflict");
}
finally{Directory.Delete(root,true);}
