using System;
using System.Threading;
using System.Threading.Tasks;
using ClamAVGuardian.Services;
using Microsoft.Extensions.Hosting;

namespace ClamAVGuardian.Service;

public class GuardianWorker : BackgroundService
{
    private GuardianContext? _context;
    private IpcHost? _ipcHost;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AppLogger.Info("ClamAV Guardian service starting.");

        _context = new GuardianContext();
        var rpcServer = new RpcServer(_context);
        _ipcHost = new IpcHost(rpcServer, _context);
        _ipcHost.Start();

        _context.StartBackgroundOperations();

        // Fire-and-forget: download+install ClamAV itself can take a while (~200MB) and
        // shouldn't block the service becoming responsive over IPC. No-ops if already found.
        _ = _context.AutoInstallClamAvIfMissingAsync();

        AppLogger.Info("ClamAV Guardian service ready.");

        try
        {
            // Check once shortly after startup, then daily — fully unattended: if a newer
            // version is published, it downloads, verifies its checksum, and installs
            // itself via silent msiexec with no prompt, since this process is already SYSTEM.
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckAndApplyUpdateAsync();
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on service stop.
        }
    }

    private static async Task CheckAndApplyUpdateAsync()
    {
        try
        {
            var result = await SelfUpdateService.CheckForUpdateAsync();
            if (result.UpdateAvailable)
            {
                AppLogger.Info($"Applying self-update to v{result.LatestVersion}.");
                await SelfUpdateService.ApplyPendingUpdateAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Background self-update check failed", ex);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        AppLogger.Info("ClamAV Guardian service stopping.");
        _ipcHost?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
