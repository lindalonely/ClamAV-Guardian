using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClamAVGuardian.Models;

namespace ClamAVGuardian.Ipc;

/// <summary>
/// RPC surface exposed by the ClamAVGuardian.Service worker over the named pipe.
/// The client calls these; the service implements them (RpcServer).
/// </summary>
public interface IGuardianService
{
    Task<bool> PingAsync();

    Task<ClamAvInstallation?> GetCurrentInstallationAsync();
    Task<ClamAvInstallation?> ApplyClamAvPathAsync(string path);
    Task<ClamAvInstallation?> LocateClamAvAsync(string? configuredPath);
    Task<InstallClamAvResult> InstallClamAvAsync();

    Task<ScanSummary> StartScanAsync(ScanRequest request, CancellationToken cancellationToken);
    Task CancelScanAsync();

    Task<UpdateStatus> GetUpdateStatusAsync();
    Task<UpdateResult> RunUpdateNowAsync(CancellationToken cancellationToken);
    Task SetUpdateCheckIntervalAsync(int hours);
    Task<int> GetUpdateCheckIntervalAsync();
    Task<bool> StartFreshClamServiceAsync();
    Task<bool> StopFreshClamServiceAsync();

    Task SetRealTimeProtectionEnabledAsync(bool enabled);
    Task<bool> IsRealTimeProtectionRunningAsync();
    Task<string> GetRealTimeEngineDescriptionAsync();

    Task<List<QuarantineEntry>> GetQuarantineEntriesAsync();
    Task<bool> RestoreQuarantineEntryAsync(string id);
    Task<bool> DeleteQuarantineEntryAsync(string id);
    Task DeleteAllQuarantineEntriesAsync();
    Task<bool> QuarantineFileAsync(string path, string threatName);

    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);

    Task<string> ReadAppLogTailAsync(int maxLines);
    Task<string?> ReadFreshClamLogAsync();

    Task<AppUpdateCheckResult> CheckForAppUpdateAsync();
    Task ApplyAppUpdateAsync();
}

/// <summary>
/// Push-notification surface the service calls on connected clients (StreamJsonRpc
/// duplex target) — scan progress, real-time detections, log lines, status changes.
/// </summary>
public interface IGuardianClientCallbacks
{
    void OnScanItem(ScanItem item);
    void OnScanStatusMessage(string message);
    void OnRealTimeThreatDetected(ScanItem item, bool quarantined);
    void OnRealTimeFileScanned(string path);
    void OnRealTimeEngineStatus(string message);
    void OnLogLine(string line);
    void OnUpdateLogLine(string line);
    void OnUpdateStatusChanged(UpdateStatus status);
    void OnClamAvInstallStatus(string message);
}
