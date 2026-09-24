using OrderedClicker.Forms;
using OrderedClicker.Services;

namespace OrderedClicker;

internal static class Program
{
#if PORTABLE
    private const bool IsPortableBuild = true;
#else
    private const bool IsPortableBuild = false;
#endif

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

        var dataPaths = AppDataPaths.Resolve(
            Environment.ProcessPath ?? Application.ExecutablePath,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            IsPortableBuild);
        using var singleInstance = new SingleInstanceCoordinator(
            $"OrderedClicker|{dataPaths.Root}");
        if (!singleInstance.IsOwner)
        {
            singleInstance.SignalActivation();
            return;
        }

        using var mainForm = new MainForm(appDataPaths: dataPaths);
        _ = mainForm.Handle;
        singleInstance.StartListening(() =>
        {
            if (mainForm.IsDisposed || !mainForm.IsHandleCreated)
            {
                return;
            }

            mainForm.BeginInvoke(mainForm.ActivateExistingInstance);
        });
        Application.Run(mainForm);
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
