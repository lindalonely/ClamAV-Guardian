using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using ClamAVGuardian.Models;
using Timer = System.Timers.Timer;

namespace ClamAVGuardian.Services;

public class UpdateService : IDisposable
{
    private readonly ClamAvInstallation _install;
    private Timer? _fallbackTimer;

    public event Action<UpdateStatus>? StatusChanged;
    public event Action<string>? LogLine;

    public UpdateService(ClamAvInstallation install)
    {
        _install = install;
    }

    public ServiceController? FindFreshClamService()
    {
        try
        {
            return ServiceController.GetServices()
                .FirstOrDefault(s =>
                    s.ServiceName.Contains("freshclam", StringComparison.OrdinalIgnoreCase) ||
                    s.DisplayName.Contains("freshclam", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    public UpdateStatus GetStatus()
    {
        var status = new UpdateStatus();

        using var svc = FindFreshClamService();
        if (svc == null)
        {
            status.ServiceState = FreshClamServiceState.NotInstalled;
        }
        else
        {
            try
            {
                svc.Refresh();
                status.ServiceState = svc.Status == ServiceControllerStatus.Running
                    ? FreshClamServiceState.Running
                    : FreshClamServiceState.Stopped;
            }
            catch
            {
                status.ServiceState = FreshClamServiceState.Unknown;
            }
        }

        status.DatabaseVersion = TryGetDatabaseVersion();
        status.LastUpdateUtc = TryGetLastUpdateTime();

        return status;
    }

    public bool TryStartService()
    {
        using var svc = FindFreshClamService();
        if (svc == null) return false;
        try
        {
            svc.Refresh();
            if (svc.Status != ServiceControllerStatus.Running)
            {
                svc.Start();
                svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to start FreshClam service", ex);
            return false;
        }
    }

    public bool TryStopService()
    {
        using var svc = FindFreshClamService();
        if (svc == null) return false;
        try
        {
            svc.Refresh();
            if (svc.Status != ServiceControllerStatus.Stopped)
            {
                svc.Stop();
                svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to stop FreshClam service", ex);
            return false;
        }
    }

    /// <summary>
    /// ClamAV's Windows installer only ships freshclam.conf.sample and never creates the
    /// database directory; freshclam refuses to run without a real freshclam.conf. This
    /// provisions a minimal working config on first use so updates work with zero manual setup.
    /// </summary>
    public (bool Success, string Message) EnsureConfigured()
    {
        try
        {
            Directory.CreateDirectory(_install.DatabaseDir);

            if (File.Exists(_install.FreshClamConfPath))
            {
                return (true, "Already configured.");
            }

            if (!File.Exists(_install.FreshClamConfSamplePath))
            {
                return (false, $"freshclam.conf is missing and no sample was found at {_install.FreshClamConfSamplePath}.");
            }

            var text = File.ReadAllText(_install.FreshClamConfSamplePath);
            text = Regex.Replace(text, @"^\s*Example\s*$", "# Example", RegexOptions.Multiline);
            text += $"{Environment.NewLine}DatabaseDirectory \"{_install.DatabaseDir}\"{Environment.NewLine}";

            File.WriteAllText(_install.FreshClamConfPath, text);
            AppLogger.Info($"Created freshclam.conf at '{_install.FreshClamConfPath}' with DatabaseDirectory '{_install.DatabaseDir}'.");
            return (true, "Created freshclam.conf from the default template.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to provision freshclam.conf", ex);
            return (false, $"Failed to set up freshclam.conf: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> RunManualUpdateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_install.FreshClamPath))
        {
            return (false, "freshclam.exe not found at configured ClamAV install path.");
        }

        var (configured, configMessage) = EnsureConfigured();
        if (!configured)
        {
            return (false, configMessage);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _install.FreshClamPath,
            Arguments = "--stdout",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var output = new StringBuilder();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            output.AppendLine(e.Data);
            LogLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            output.AppendLine(e.Data);
            LogLine?.Invoke(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            // freshclam exit code 1 commonly just means "already up to date" in older builds;
            // treat 0 as success and inspect output text for a clearer signal either way.
            var text = output.ToString();
            var success = process.ExitCode == 0 ||
                          text.Contains("up to date", StringComparison.OrdinalIgnoreCase) ||
                          text.Contains("updated", StringComparison.OrdinalIgnoreCase);

            StatusChanged?.Invoke(GetStatus());
            return (success, success ? "Virus database is up to date." : text);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void StartFallbackTimer(int intervalHours)
    {
        StopFallbackTimer();
        _fallbackTimer = new Timer(TimeSpan.FromHours(Math.Max(1, intervalHours)).TotalMilliseconds)
        {
            AutoReset = true,
        };
        _fallbackTimer.Elapsed += (_, _) => _ = RunFallbackTickAsync();
        _fallbackTimer.Start();
    }

    private async Task RunFallbackTickAsync()
    {
        try
        {
            var svc = FindFreshClamService();
            var serviceRunning = false;
            try { svc?.Refresh(); serviceRunning = svc?.Status == ServiceControllerStatus.Running; }
            finally { svc?.Dispose(); }

            if (!serviceRunning)
            {
                await RunManualUpdateAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // This runs on a Timer callback inside a long-lived Windows Service — an
            // unhandled exception here would take down the whole protection engine, not
            // just show a dialog like it did in the old desktop-app build.
            AppLogger.Error("Fallback update timer tick failed", ex);
        }
    }

    public void StopFallbackTimer()
    {
        _fallbackTimer?.Stop();
        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
    }

    public int GetCheckIntervalHours()
    {
        try
        {
            if (!File.Exists(_install.FreshClamConfPath)) return 2;
            var text = File.ReadAllText(_install.FreshClamConfPath);
            var match = Regex.Match(text, @"^\s*Checks\s*=\s*(\d+)", RegexOptions.Multiline);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var checksPerDay) && checksPerDay > 0)
            {
                return Math.Max(1, 24 / checksPerDay);
            }
        }
        catch
        {
            // fall through to default
        }

        return 2;
    }

    public bool SetCheckIntervalHours(int hours)
    {
        try
        {
            if (!File.Exists(_install.FreshClamConfPath)) return false;
            var checksPerDay = Math.Clamp(24 / Math.Max(1, hours), 1, 50);
            var text = File.ReadAllText(_install.FreshClamConfPath);

            text = Regex.IsMatch(text, @"^\s*Checks\s*=\s*\d+", RegexOptions.Multiline)
                ? Regex.Replace(text, @"^\s*Checks\s*=\s*\d+", $"Checks = {checksPerDay}", RegexOptions.Multiline)
                : text + $"{Environment.NewLine}Checks = {checksPerDay}{Environment.NewLine}";

            File.WriteAllText(_install.FreshClamConfPath, text);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to update Checks interval in freshclam.conf", ex);
            return false;
        }
    }

    private string? TryGetDatabaseVersion()
    {
        try
        {
            if (!File.Exists(_install.ClamScanPath)) return null;

            var psi = new ProcessStartInfo
            {
                FileName = _install.ClamScanPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output.Trim();
        }
        catch
        {
            return null;
        }
    }

    private DateTime? TryGetLastUpdateTime()
    {
        try
        {
            var dbDir = _install.DatabaseDir;
            if (!Directory.Exists(dbDir)) return null;

            var newest = Directory.GetFiles(dbDir, "*.cvd")
                .Concat(Directory.GetFiles(dbDir, "*.cld"))
                .Select(f => new FileInfo(f).LastWriteTimeUtc)
                .DefaultIfEmpty()
                .Max();

            return newest == default ? null : newest;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        StopFallbackTimer();
    }
}
