using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ClamAVGuardian.Ipc;
using ClamAVGuardian.Models;
using StreamJsonRpc;

namespace ClamAVGuardian.Client.Ipc;

/// <summary>
/// Connects to the ClamAVGuardian.Service named pipe and exposes both an RPC proxy for
/// calling into it and a target for the events it pushes back. Reconnects with backoff —
/// the service can be briefly unavailable at client startup or mid self-update.
/// </summary>
public class GuardianServiceClient : IGuardianClientCallbacks, IDisposable
{
    private JsonRpc? _rpc;
    private IGuardianService? _proxy;
    private CancellationTokenSource? _connectLoopCts;

    public event Action<ScanItem>? ScanItemScanned;
    public event Action<string>? ScanStatusMessage;
    public event Action<ScanItem, bool>? RealTimeThreatDetected;
    public event Action<string>? RealTimeFileScanned;
    public event Action<string>? RealTimeEngineStatus;
    public event Action<string>? LogLine;
    public event Action<string>? UpdateLogLine;
    public event Action<bool>? ConnectionStateChanged;

    public bool IsConnected => _proxy != null;

    public IGuardianService Service =>
        _proxy ?? throw new InvalidOperationException("Not connected to the ClamAV Guardian service yet.");

    public void Start()
    {
        _connectLoopCts = new CancellationTokenSource();
        _ = ConnectLoopAsync(_connectLoopCts.Token);
    }

    private async Task ConnectLoopAsync(CancellationToken token)
    {
        var delay = TimeSpan.FromSeconds(1);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", PipeNames.ServicePipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(5000, token);

                var rpc = new JsonRpc(pipe, pipe);
                rpc.AddLocalRpcTarget<IGuardianClientCallbacks>(this, null);
                rpc.StartListening();

                _rpc = rpc;
                _proxy = rpc.Attach<IGuardianService>();

                await _proxy.PingAsync();

                ConnectionStateChanged?.Invoke(true);
                delay = TimeSpan.FromSeconds(1);

                await rpc.Completion;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Service not up yet (or restarting mid self-update) — retry with backoff.
            }
            finally
            {
                _proxy = null;
                _rpc?.Dispose();
                _rpc = null;
                ConnectionStateChanged?.Invoke(false);
            }

            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 15));
        }
    }

    public void OnScanItem(ScanItem item) => ScanItemScanned?.Invoke(item);
    public void OnScanStatusMessage(string message) => ScanStatusMessage?.Invoke(message);
    public void OnRealTimeThreatDetected(ScanItem item, bool quarantined) => RealTimeThreatDetected?.Invoke(item, quarantined);
    public void OnRealTimeFileScanned(string path) => RealTimeFileScanned?.Invoke(path);
    public void OnRealTimeEngineStatus(string message) => RealTimeEngineStatus?.Invoke(message);
    public void OnLogLine(string line) => LogLine?.Invoke(line);
    public void OnUpdateLogLine(string line) => UpdateLogLine?.Invoke(line);
    public void OnUpdateStatusChanged(UpdateStatus status) { /* clients poll GetUpdateStatusAsync on demand today */ }

    public void Dispose()
    {
        _connectLoopCts?.Cancel();
        _rpc?.Dispose();
    }
}
