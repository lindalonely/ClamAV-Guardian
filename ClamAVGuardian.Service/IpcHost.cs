using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using ClamAVGuardian.Ipc;
using ClamAVGuardian.Services;
using StreamJsonRpc;

namespace ClamAVGuardian.Service;

/// <summary>
/// Named-pipe server hosting the RPC contract. The pipe is a control channel into a
/// SYSTEM-level process, so it's ACL-restricted to Administrators/interactive local users
/// and explicitly denied over the network — an unauthenticated channel here would let any
/// local process drive privileged file operations through it.
/// </summary>
public class IpcHost : IDisposable
{
    private readonly RpcServer _rpcServer;
    private readonly GuardianContext _context;
    private readonly List<JsonRpc> _connections = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public IpcHost(RpcServer rpcServer, GuardianContext context)
    {
        _rpcServer = rpcServer;
        _context = context;

        _context.ScanItemScanned += item => Broadcast(cb => cb.OnScanItem(item));
        _context.ScanStatusMessage += msg => Broadcast(cb => cb.OnScanStatusMessage(msg));
        _context.RealTimeThreatDetected += (item, quarantined) => Broadcast(cb => cb.OnRealTimeThreatDetected(item, quarantined));
        _context.RealTimeFileScanned += path => Broadcast(cb => cb.OnRealTimeFileScanned(path));
        _context.RealTimeEngineStatus += msg => Broadcast(cb => cb.OnRealTimeEngineStatus(msg));
        _context.LogLine += line => Broadcast(cb => cb.OnLogLine(line));
        _context.UpdateLogLine += line => Broadcast(cb => cb.OnUpdateLogLine(line));
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_lock)
        {
            foreach (var conn in _connections) conn.Dispose();
            _connections.Clear();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe;
            try
            {
                pipe = CreatePipeServer();
                await pipe.WaitForConnectionAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Error("IPC pipe accept failed", ex);
                await Task.Delay(1000, token);
                continue;
            }

            _ = HandleConnectionAsync(pipe);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe)
    {
        var rpc = new JsonRpc(pipe, pipe);
        rpc.AddLocalRpcTarget(_rpcServer);

        lock (_lock) { _connections.Add(rpc); }
        AppLogger.Info("Client connected over IPC.");

        rpc.Disconnected += (_, _) =>
        {
            lock (_lock) { _connections.Remove(rpc); }
            pipe.Dispose();
            AppLogger.Info("Client disconnected from IPC.");
        };

        rpc.StartListening();

        try { await rpc.Completion; }
        catch { /* connection closed/reset — expected on client exit */ }
    }

    private static NamedPipeServerStream CreatePipeServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            PipeNames.ServicePipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private void Broadcast(Action<IGuardianClientCallbacks> action)
    {
        List<JsonRpc> snapshot;
        lock (_lock) { snapshot = _connections.ToList(); }

        foreach (var rpc in snapshot)
        {
            try
            {
                action(rpc.Attach<IGuardianClientCallbacks>());
            }
            catch
            {
                // Connection may have just dropped between the snapshot and this call; ignore.
            }
        }
    }

    public void Dispose() => Stop();
}
