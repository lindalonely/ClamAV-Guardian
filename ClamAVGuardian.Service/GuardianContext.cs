using System;
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
    public QuarantineService QuarantineService { get; }

    public event Action<ScanItem>? ScanItemScanned;
    public event Action<string>? ScanStatusMessage;
    public event Action<ScanItem, bool>? RealTimeThreatDetected;
    public event Action<string>? RealTimeFileScanned;
    public event Action<string>? RealTimeEngineStatus;
    public event Action<string>? LogLine;
    public event Action<string>? UpdateLogLine;

    public GuardianContext()
    {
        Settings = SettingsManager.Load();
        QuarantineService = new QuarantineService(
            string.IsNullOrWhiteSpace(Settings.QuarantinePath) ? AppSettings.DefaultQuarantinePath : Settings.QuarantinePath);

        AppLogger.LineWritten += line => LogLine?.Invoke(line);

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

    public ClamAvInstallation? ApplyClamAvPath(string path)
    {
        var candidate = ClamAvLocator.TryLocate(path);
        if (candidate == null) return null;

        Install = candidate;
        Settings.ClamAvInstallPath = candidate.InstallDir;
        SettingsManager.Save(Settings);
        WireUpInstallDependentServices();
        UpdateService?.StartFallbackTimer(Settings.UpdateCheckIntervalHours > 0 ? Settings.UpdateCheckIntervalHours : 2);
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

        AppLogger.Info($"ClamAV located at '{Install.InstallDir}'.");
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
