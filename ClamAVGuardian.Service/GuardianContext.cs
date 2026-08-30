using System;
using System.Threading.Tasks;
using ClamAVGuardian.Ipc;
using ClamAVGuardian.Models;
using ClamAVGuardian.Services;

namespace ClamAVGuardian.Service;

/// <summary>
/// Owns the long-lived scanning/update/protection state for the whole service process —
/// the direct successor to what MainForm used to wire up for itself, minus any UI coupling.
/// </summary>
public class GuardianContext
{
    public AppSettings Settings { get; private set; }
    public ClamAvInstallation? Install { get; private set; }
    public ScanService? ScanService { get; private set; }
    public UpdateService? UpdateService { get; private set; }
    public RealTimeProtectionService? RealTimeService { get; private set; }
    public ClamdService? ClamdService { get; private set; }
    public QuarantineService QuarantineService { get; }

    public event Action<ScanItem>? ScanItemScanned;
    public event Action<string>? ScanStatusMessage;
    public event Action<ScanItem, bool>? RealTimeThreatDetected;
    public event Action<string>? RealTimeFileScanned;
    public event Action<string>? RealTimeEngineStatus;
    public event Action<string>? LogLine;
    public event Action<string>? UpdateLogLine;
    public event Action<DownloadProgress>? ClamAvInstallProgress;
    public event Action<DownloadProgress>? AppUpdateProgress;

    private bool _clamAvInstallInProgress;

    public GuardianContext()
    {
        Settings = SettingsManager.Load();
        QuarantineService = new QuarantineService(
            string.IsNullOrWhiteSpace(Settings.QuarantinePath) ? AppSettings.DefaultQuarantinePath : Settings.QuarantinePath);

        AppLogger.LineWritten += line => LogLine?.Invoke(line);
        SelfUpdateService.ProgressChanged += progress => AppUpdateProgress?.Invoke(progress);

        Install = ClamAvLocator.TryLocate(Settings.ClamAvInstallPath);
        if (Install != null)
        {
            WireUpInstallDependentServices();
        }
        else
        {
            AppLogger.Warn("ClamAV not found at startup. Set the install path from the client's Settings tab.");
        }
    }

    /// <summary>Called once by the worker after construction, so timers/watchers only start once.</summary>
    public void StartBackgroundOperations()
    {
        if (Settings.RealTimeProtectionEnabled && RealTimeService != null)
        {
            _ = RealTimeService.StartAsync(Settings.RealTimeWatchedFolders);
        }

        UpdateService?.StartFallbackTimer(Settings.UpdateCheckIntervalHours > 0 ? Settings.UpdateCheckIntervalHours : 2);
    }

    /// <summary>
    /// Called once by the worker shortly after startup (fire-and-forget — download+install
    /// can take a while and shouldn't block the service becoming responsive). No-ops if
    /// ClamAV is already found or an install attempt is already running.
    /// </summary>
    public async Task AutoInstallClamAvIfMissingAsync()
    {
        if (Install != null || _clamAvInstallInProgress) return;
        AppLogger.Info("ClamAV not found — attempting automatic install.");
        await InstallClamAvAsync();
    }

    public async Task<(bool Success, string Message)> InstallClamAvAsync()
    {
        if (_clamAvInstallInProgress)
        {
            return (false, "An install is already in progress.");
        }

        _clamAvInstallInProgress = true;
        void OnProgress(DownloadProgress progress) => ClamAvInstallProgress?.Invoke(progress);
        ClamAvInstallerService.ProgressChanged += OnProgress;

        try
        {
            var (success, message) = await ClamAvInstallerService.DownloadAndInstallAsync();
            if (success)
            {
                var candidate = ClamAvLocator.TryLocate(null);
                if (candidate != null)
                {
                    Install = candidate;
                    Settings.ClamAvInstallPath = candidate.InstallDir;
                    SettingsManager.Save(Settings);
                    WireUpInstallDependentServices();
                    UpdateService?.StartFallbackTimer(Settings.UpdateCheckIntervalHours > 0 ? Settings.UpdateCheckIntervalHours : 2);
                    if (Settings.RealTimeProtectionEnabled && RealTimeService != null)
                    {
                        _ = RealTimeService.StartAsync(Settings.RealTimeWatchedFolders);
                    }
                }
                else
                {
                    AppLogger.Warn("ClamAV installer reported success but the engine still wasn't found afterward.");
                }
            }

            return (success, message);
        }
        finally
        {
            ClamAvInstallerService.ProgressChanged -= OnProgress;
            _clamAvInstallInProgress = false;
        }
    }

    public ClamAvInstallation? ApplyClamAvPath(string path)
    {
        var candidate = ClamAvLocator.TryLocate(path);
        if (candidate == null) return null;

        Install = candidate;
        Settings.ClamAvInstallPath = candidate.InstallDir;
        SettingsManager.Save(Settings);
        WireUpInstallDependentServices();
        UpdateService?.StartFallbackTimer(Settings.UpdateCheckIntervalHours > 0 ? Settings.UpdateCheckIntervalHours : 2);
        if (Settings.RealTimeProtectionEnabled && RealTimeService != null)
        {
            _ = RealTimeService.StartAsync(Settings.RealTimeWatchedFolders);
        }
        return candidate;
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        SettingsManager.Save(Settings);
        if (RealTimeService != null)
        {
            RealTimeService.AutoQuarantine = Settings.AutoQuarantineOnDetection;
        }
    }

    private void WireUpInstallDependentServices()
    {
        if (Install == null) return;

        ScanService = new ScanService(Install);
        ScanService.ItemScanned += item => ScanItemScanned?.Invoke(ApplyAutoQuarantineIfNeeded(item));
        ScanService.StatusMessage += msg => ScanStatusMessage?.Invoke(msg);

        UpdateService = new UpdateService(Install);
        UpdateService.LogLine += line => UpdateLogLine?.Invoke(line);
        var (configured, message) = UpdateService.EnsureConfigured();
        if (!configured) AppLogger.Warn($"freshclam configuration issue: {message}");

        RealTimeService = new RealTimeProtectionService(Install, QuarantineService)
        {
            AutoQuarantine = Settings.AutoQuarantineOnDetection,
        };
        RealTimeService.ThreatDetected += (item, quarantined) => RealTimeThreatDetected?.Invoke(item, quarantined);
        RealTimeService.FileScanned += path => RealTimeFileScanned?.Invoke(path);
        RealTimeService.StatusMessage += msg => RealTimeEngineStatus?.Invoke(msg);

        ClamdService = new ClamdService(Install);

        AppLogger.Info($"ClamAV located at '{Install.InstallDir}'.");
    }

    /// <summary>
    /// Registers and starts clamd as a Windows service, so real-time protection can use the
    /// resident daemon over TCP instead of spawning a clamscan process per file. If real-time
    /// protection is already running, restarts it afterward so it picks up clamd immediately
    /// rather than waiting for the next manual toggle.
    /// </summary>
    public async Task<(bool Success, string Message)> InstallClamdAsync()
    {
        if (ClamdService == null)
        {
            return (false, "ClamAV is not configured yet.");
        }

        var (success, message) = await ClamdService.InstallAndStartAsync();

        if (success && RealTimeService is { IsRunning: true })
        {
            _ = RealTimeService.StartAsync(Settings.RealTimeWatchedFolders);
        }

        return (success, message);
    }

    /// <summary>
    /// Manual scans used to have the client quarantine infected files itself; that logic
    /// now lives here, since the server owns both scanning and quarantine and the client
    /// should just be told what happened. Returns a copy of the item flagged accordingly.
    /// </summary>
    private ScanItem ApplyAutoQuarantineIfNeeded(ScanItem item)
    {
        if (item.Status != ScanStatus.Infected || !Settings.AutoQuarantineOnDetection)
        {
            return item;
        }

        var entry = QuarantineService.QuarantineFile(item.Path, item.ThreatName ?? "Unknown");
        return new ScanItem
        {
            Path = item.Path,
            Status = item.Status,
            ThreatName = item.ThreatName,
            ErrorMessage = item.ErrorMessage,
            ScannedAtUtc = item.ScannedAtUtc,
            WasQuarantined = entry != null,
        };
    }
}
