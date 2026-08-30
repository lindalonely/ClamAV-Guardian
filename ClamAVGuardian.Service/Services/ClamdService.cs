using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClamAVGuardian.Models;

namespace ClamAVGuardian.Services;

/// <summary>
/// Installs and manages clamd (ClamAV's resident scanning daemon) as a native Windows
/// service, so real-time protection can use it instead of spawning a fresh clamscan process
/// per file — clamd keeps the virus database loaded once and answers scan requests over a
/// local TCP socket, which is what ClamdClient already talks to on 127.0.0.1:3310.
/// </summary>
public class ClamdService
{
    private const string ServiceName = "ClamD";
    private readonly ClamAvInstallation _install;

    public ClamdService(ClamAvInstallation install)
    {
        _install = install;
    }

    public ServiceController? FindService()
    {
        try
        {
            return ServiceController.GetServices()
                .FirstOrDefault(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    public ClamdServiceState GetState()
    {
        using var svc = FindService();
        if (svc == null) return ClamdServiceState.NotInstalled;

        try
        {
            svc.Refresh();
            return svc.Status == ServiceControllerStatus.Running ? ClamdServiceState.Running : ClamdServiceState.Stopped;
        }
        catch
        {
            return ClamdServiceState.Unknown;
        }
    }

    /// <summary>
    /// clamd.conf.sample ships entirely commented out (every directive prefixed by a line
    /// that just says "Example"), so clamd refuses to start until a real config exists. This
    /// provisions one wired to the TCP socket ClamdClient expects, with zero manual setup.
    /// </summary>
    public (bool Success, string Message) EnsureConfigured()
    {
        try
        {
            if (File.Exists(_install.ClamdConfPath))
            {
                return (true, "Already configured.");
            }

            if (!File.Exists(_install.ClamdConfSamplePath))
            {
                return (false, $"clamd.conf is missing and no sample was found at {_install.ClamdConfSamplePath}.");
            }

            var text = File.ReadAllText(_install.ClamdConfSamplePath);
            text = Regex.Replace(text, @"^\s*Example\s*$", "# Example", RegexOptions.Multiline);
            text += Environment.NewLine +
                    $"DatabaseDirectory \"{_install.DatabaseDir}\"{Environment.NewLine}" +
                    "TCPSocket 3310" + Environment.NewLine +
                    "TCPAddr 127.0.0.1" + Environment.NewLine +
                    $"LogFile \"{_install.ClamdLogPath}\"{Environment.NewLine}" +
                    $"PidFile \"{_install.ClamdPidPath}\"{Environment.NewLine}";

            File.WriteAllText(_install.ClamdConfPath, text);
            AppLogger.Info($"Created clamd.conf at '{_install.ClamdConfPath}'.");
            return (true, "Created clamd.conf from the default template.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to provision clamd.conf", ex);
            return (false, $"Failed to set up clamd.conf: {ex.Message}");
        }
    }

    /// <summary>
    /// Registers clamd as a native Windows service (auto-start) and starts it. Safe to call
    /// repeatedly — no-ops if the service already exists, just (re)starts it if needed.
    /// </summary>
    public async Task<(bool Success, string Message)> InstallAndStartAsync()
    {
        if (!File.Exists(_install.ClamdPath))
        {
            return (false, "clamd.exe was not found in the ClamAV install directory.");
        }

        var (configured, configMessage) = EnsureConfigured();
        if (!configured)
        {
            return (false, configMessage);
        }

        if (FindService() == null)
        {
            var binPath = $"\"{_install.ClamdPath}\" --config-file=\"{_install.ClamdConfPath}\"";
            var scArgs = $"create {ServiceName} binPath= \"{binPath}\" start= auto DisplayName= \"ClamAV clamd\"";

            var (exitCode, output) = await RunProcessAsync("sc.exe", scArgs);
            if (exitCode != 0)
            {
                AppLogger.Error($"sc create failed for clamd (exit {exitCode}): {output}");
                return (false, $"Failed to register the clamd service: {output.Trim()}");
            }

            AppLogger.Info("Registered clamd as a Windows service.");
        }

        using var svc = FindService();
        if (svc == null)
        {
            return (false, "clamd service registration reported success, but the service still wasn't found.");
        }

        try
        {
            svc.Refresh();
            if (svc.Status != ServiceControllerStatus.Running)
            {
                svc.Start();
                svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
            return (true, "clamd is installed and running.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to start clamd service", ex);
            return (false, $"clamd was registered but failed to start: {ex.Message}");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null) return (-1, "Failed to start process.");

        var output = new StringBuilder();
        output.Append(await process.StandardOutput.ReadToEndAsync());
        output.Append(await process.StandardError.ReadToEndAsync());
        await process.WaitForExitAsync();

        return (process.ExitCode, output.ToString());
    }
}
