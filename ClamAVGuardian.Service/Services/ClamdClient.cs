using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClamAVGuardian.Services;

public class ClamdScanResult
{
    public bool Infected { get; init; }
    public string? ThreatName { get; init; }
    public bool Success { get; init; }
    public string? RawResponse { get; init; }
}

public class ClamdClient
{
    private readonly string _host;
    private readonly int _port;
    private const int ChunkSize = 8192;

    public ClamdClient(string host = "127.0.0.1", int port = 3310)
    {
        _host = host;
        _port = port;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(_host, _port);
            var timeoutTask = Task.Delay(1500, cancellationToken);
            if (await Task.WhenAny(connectTask, timeoutTask) != connectTask) return false;
            if (!client.Connected) return false;

            using var stream = client.GetStream();
            await SendCommandAsync(stream, "PING", cancellationToken);
            var response = await ReadResponseAsync(stream, cancellationToken);
            return response.Contains("PONG", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, cancellationToken);
            using var stream = client.GetStream();
            await SendCommandAsync(stream, "VERSION", cancellationToken);
            return await ReadResponseAsync(stream, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ClamdScanResult> ScanFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, cancellationToken);
            using var stream = client.GetStream();

            await SendCommandAsync(stream, "INSTREAM", cancellationToken);

            await using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var buffer = new byte[ChunkSize];
                int read;
                while ((read = await fileStream.ReadAsync(buffer.AsMemory(0, ChunkSize), cancellationToken)) > 0)
                {
                    var lengthPrefix = BitConverter.GetBytes(read);
                    if (BitConverter.IsLittleEndian) Array.Reverse(lengthPrefix);
                    await stream.WriteAsync(lengthPrefix, cancellationToken);
                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            var zeroChunk = BitConverter.GetBytes(0);
            if (BitConverter.IsLittleEndian) Array.Reverse(zeroChunk);
            await stream.WriteAsync(zeroChunk, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var response = await ReadResponseAsync(stream, cancellationToken);
            return ParseScanResponse(response);
        }
        catch (Exception ex)
        {
            return new ClamdScanResult { Success = false, RawResponse = ex.Message };
        }
    }

    private static ClamdScanResult ParseScanResponse(string response)
    {
        // Formats: "stream: OK" or "stream: Win.Test.EICAR_HDB-1 FOUND" or "stream: ... ERROR"
        if (response.Contains("FOUND", StringComparison.Ordinal))
        {
            var idx = response.IndexOf(':');
            var afterColon = idx >= 0 ? response[(idx + 1)..].Trim() : response;
            var threatName = afterColon.EndsWith("FOUND", StringComparison.Ordinal)
                ? afterColon[..^"FOUND".Length].Trim()
                : afterColon;
            return new ClamdScanResult { Success = true, Infected = true, ThreatName = threatName, RawResponse = response };
        }

        if (response.Contains("OK", StringComparison.Ordinal))
        {
            return new ClamdScanResult { Success = true, Infected = false, RawResponse = response };
        }

        return new ClamdScanResult { Success = false, RawResponse = response };
    }

    private static async Task SendCommandAsync(NetworkStream stream, string command, CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes($"z{command}\0");
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        stream.ReadTimeout = 30000;

        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, cancellationToken);
            }
            catch (IOException)
            {
                break;
            }

            if (read <= 0) break;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, read));

            if (sb.ToString().Contains('\0') || !stream.DataAvailable) break;
        }

        return sb.ToString().TrimEnd('\0', '\r', '\n');
    }
}
