namespace OrderedClicker.Tests;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length == 2 && args[0] == "--render-ui")
        {
            UiSmokeTests.Render(args[1]);
            return 0;
        }

        if (args.Length == 2 && args[0] == "--render-settings")
        {
            UiSmokeTests.RenderSettings(args[1]);
            return 0;
        }

        if (args.Length == 2 && args[0] == "--render-help")
        {
            UiSmokeTests.RenderHelp(args[1]);
            return 0;
        }

        if (args.Length == 3
            && args[0] == "--render-theme"
            && Enum.TryParse<Theming.AppThemeId>(args[1], true, out var themeId))
        {
            UiSmokeTests.RenderTheme(themeId, args[2]);
            return 0;
        }

        var tests = new (string Name, Func<Task> Run)[]
        {
            ("ProfileValidator", () =>
            {
                ProfileValidatorTests.Run();
                return Task.CompletedTask;
            }),
            ("PointTimingService", () =>
            {
                PointTimingServiceTests.Run();
                return Task.CompletedTask;
            }),
            ("HotKeyBindingService", () =>
            {
                HotKeyBindingServiceTests.Run();
                return Task.CompletedTask;
            }),
            ("ClickExecutionEngine", ClickExecutionEngineTests.RunAsync),
            ("ProfileService", () =>
            {
                ProfileServiceTests.Run();
                return Task.CompletedTask;
            }),
            ("ThemeSettings", () =>
            {
                ThemeSettingsTests.Run();
                return Task.CompletedTask;
            }),
            ("UiSmoke", () =>
            {
                UiSmokeTests.Run();
                return Task.CompletedTask;
            })
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed");
        return failed == 0 ? 0 : 1;
    }
}
