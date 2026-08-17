using ClamAVGuardian.Forms;
using ClamAVGuardian.Services;

namespace ClamAVGuardian;

static class Program
{
    // Per-user (not Global\) single-instance guard — the client is unelevated and per-user
    // now, unlike the old single elevated process; Global\ mutex creation can also fail
    // for non-admin users in restricted (e.g. Terminal Services) environments.
    private const string MutexName = "ClamAVGuardian_Client_SingleInstance";

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "ClamAV Guardian is already running. Check your system tray.",
                "ClamAV Guardian",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ClientLogger.Info("Application starting.");

        Application.ThreadException += (_, e) =>
            ClientLogger.Error("Unhandled UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ClientLogger.Error("Unhandled AppDomain exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            ClientLogger.Error("Unhandled exception in application", ex);
            MessageBox.Show(
                $"A fatal error occurred: {ex.Message}",
                "ClamAV Guardian",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        ClientLogger.Info("Application exiting.");
    }
}
