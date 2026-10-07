using Avalonia;
using OrderedClicker.Mac.Platform;
using OrderedClicker.Services;

namespace OrderedClicker.Mac;

internal static class Program
{
    public static AppDataPaths Paths { get; private set; } = null!;
    public static string? RenderDirectory { get; private set; }
    public static bool Preview => RenderDirectory != null;
    public static bool NativeProbe { get; private set; }
    [STAThread]
    public static int Main(string[] args)
    {
        NativeProbe = args.Contains("--native-probe");
        if (args.Length >= 2 && args[0] == "--render-ui") RenderDirectory = Path.GetFullPath(args[1]);
        var root = Preview ? Path.Combine(RenderDirectory!, "preview-data") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "OrderedClicker");
        Paths = AppDataPaths.Create(root, false);
        using var instance = new MacInstance(root);
        if (!instance.IsOwner) { MacNative.oc_activate_existing(); return 0; }
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { new DiagnosticLogService(Paths).Write("fatal", e.ExceptionObject as Exception ?? new Exception("未知错误")); }
            catch { }
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
