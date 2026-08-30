using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClamAVGuardian.Ipc;
using ClamAVGuardian.Models;
using ClamAVGuardian.Services;

namespace ClamAVGuardian.Service;

/// <summary>
/// Implements the client-facing RPC contract. One instance is shared across all connected
/// clients (StreamJsonRpc attaches it as the target for every pipe connection) and simply
/// delegates to GuardianContext, which owns the actual long-lived state.
/// </summary>
public class RpcServer : IGuardianService
{
    private readonly GuardianContext _context;
    private CancellationTokenSource? _scanCts;

    public RpcServer(GuardianContext context)
    {
        _context = context;
    }

    public Task<bool> PingAsync() => Task.FromResult(true);

    public Task<ClamAvInstallation?> GetCurrentInstallationAsync() => Task.FromResult(_context.Install);

    public Task<ClamAvInstallation?> ApplyClamAvPathAsync(string path) => Task.FromResult(_context.ApplyClamAvPath(path));

    public Task<ClamAvInstallation?> LocateClamAvAsync(string? configuredPath) => Task.FromResult(ClamAvLocator.TryLocate(configuredPath));

    public async Task<InstallClamAvResult> InstallClamAvAsync()
    {
        var (success, message) = await _context.InstallClamAvAsync();
        return new InstallClamAvResult { Success = success, Message = message };
    }

    public async Task<ScanSummary> StartScanAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        if (_context.ScanService == null || _context.Install == null)
        {
            return new ScanSummary { DatabaseMissing = true };
        }

        var targets = request.Kind switch
        {
            ScanKind.Full => ScanService.BuildFullScanTargets(),
            ScanKind.Custom when !string.IsNullOrWhiteSpace(request.CustomPath) && Directory.Exists(request.CustomPath)
                => new List<string> { request.CustomPath! },
            _ => ScanService.BuildQuickScanTargets(),
        };

        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return await _context.ScanService.RunScanAsync(
            targets, _context.Settings.ScanExclusionPaths, _context.Settings.ScanExclusionExtensions, _scanCts.Token);
    }

    public async Task CancelScanAsync()
    {
        if (_scanCts != null)
        {
            await _scanCts.CancelAsync();
        }
    }

    public Task<UpdateStatus> GetUpdateStatusAsync()
    {
        if (_context.UpdateService == null) return Task.FromResult(new UpdateStatus());
        return Task.FromResult(_context.UpdateService.GetStatus());
    }

    public async Task<UpdateResult> RunUpdateNowAsync(CancellationToken cancellationToken)
    {
        if (_context.UpdateService == null)
        {
            return new UpdateResult { Success = false, Message = "ClamAV is not configured yet." };
        }

        var (success, message) = await _context.UpdateService.RunManualUpdateAsync(cancellationToken);
        return new UpdateResult { Success = success, Message = message };
    }

    public Task SetUpdateCheckIntervalAsync(int hours)
    {
        _context.Settings.UpdateCheckIntervalHours = hours;
        SettingsManager.Save(_context.Settings);
        _context.UpdateService?.SetCheckIntervalHours(hours);
        _context.UpdateService?.StartFallbackTimer(hours);
        return Task.CompletedTask;
    }

    public Task<int> GetUpdateCheckIntervalAsync() =>
        Task.FromResult(_context.UpdateService?.GetCheckIntervalHours() ?? _context.Settings.UpdateCheckIntervalHours);

    public Task<bool> StartFreshClamServiceAsync() => Task.FromResult(_context.UpdateService?.TryStartService() ?? false);

    public Task<bool> StopFreshClamServiceAsync() => Task.FromResult(_context.UpdateService?.TryStopService() ?? false);

    public async Task SetRealTimeProtectionEnabledAsync(bool enabled)
    {
        if (_context.RealTimeService == null) return;

        if (enabled)
        {
            await _context.RealTimeService.StartAsync(_context.Settings.RealTimeWatchedFolders);
        }
        else
        {
            _context.RealTimeService.Stop();
        }

        _context.Settings.RealTimeProtectionEnabled = enabled;
        SettingsManager.Save(_context.Settings);
    }

    public Task<bool> IsRealTimeProtectionRunningAsync() => Task.FromResult(_context.RealTimeService?.IsRunning ?? false);

    public Task<string> GetRealTimeEngineDescriptionAsync() =>
        Task.FromResult(_context.RealTimeService?.EngineDescription ?? "inactive");

    public Task<ClamdServiceState> GetClamdStateAsync() =>
        Task.FromResult(_context.ClamdService?.GetState() ?? ClamdServiceState.NotInstalled);

    public async Task<ClamdActionResult> InstallClamdAsync()
    {
        var (success, message) = await _context.InstallClamdAsync();
        return new ClamdActionResult { Success = success, Message = message };
    }

    public Task<List<QuarantineEntry>> GetQuarantineEntriesAsync() =>
        Task.FromResult(_context.QuarantineService.LoadEntries().OrderByDescending(e => e.QuarantinedAtUtc).ToList());

    public Task<bool> RestoreQuarantineEntryAsync(string id) => Task.FromResult(_context.QuarantineService.Restore(id));

    public Task<bool> DeleteQuarantineEntryAsync(string id) => Task.FromResult(_context.QuarantineService.DeletePermanently(id));

    public Task DeleteAllQuarantineEntriesAsync()
    {
        _context.QuarantineService.DeleteAll();
        return Task.CompletedTask;
    }

    public Task<bool> QuarantineFileAsync(string path, string threatName) =>
        Task.FromResult(_context.QuarantineService.QuarantineFile(path, threatName) != null);

    public Task<AppSettings> GetSettingsAsync() => Task.FromResult(_context.Settings);

    public Task SaveSettingsAsync(AppSettings settings)
    {
        _context.SaveSettings(settings);
        return Task.CompletedTask;
    }

    public Task<string> ReadAppLogTailAsync(int maxLines) => Task.FromResult(AppLogger.ReadTail(maxLines));

    public Task<string?> ReadFreshClamLogAsync()
    {
        if (_context.Install == null) return Task.FromResult<string?>(null);
        var freshclamLog = Path.Combine(_context.Install.InstallDir, "freshclam.log");
        return Task.FromResult<string?>(File.Exists(freshclamLog) ? File.ReadAllText(freshclamLog) : null);
    }

    public Task<AppUpdateCheckResult> CheckForAppUpdateAsync() =>
        SelfUpdateService.CheckForUpdateAsync();

    public Task ApplyAppUpdateAsync() => SelfUpdateService.ApplyPendingUpdateAsync();
}
