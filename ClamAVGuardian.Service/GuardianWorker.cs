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

        AppLogger.Info("ClamAV Guardian service ready.");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on service stop.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        AppLogger.Info("ClamAV Guardian service stopping.");
        _ipcHost?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
