using OrderedClicker.Forms;

namespace OrderedClicker;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, eventArgs) =>
            ShowFatalError(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            ShowFatalError(eventArgs.ExceptionObject as Exception);

        Application.Run(new MainForm());
    }

    private static void ShowFatalError(Exception? exception)
    {
        MessageBox.Show(
            exception?.Message ?? "发生未知错误。",
            "连点器错误",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
