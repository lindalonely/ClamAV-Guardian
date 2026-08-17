using System;
using System.IO;

namespace ClamAVGuardian.Services;

/// <summary>
/// Client-side counterpart to the service's AppLogger (which moved into
/// ClamAVGuardian.Service and now owns the meaningful scan/update/protection log).
/// This covers purely client-local events — tray/UI issues, IPC connection state —
/// stored per-user since the client no longer runs elevated.
/// </summary>
public static class ClientLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClamAVGuardian", "logs");

    private static readonly string LogFile = Path.Combine(LogDir, "client.log");
    private static readonly object Lock = new();

    public static event Action<string>? LineWritten;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFile, line + Environment.NewLine);

                var info = new FileInfo(LogFile);
                if (info.Length > 2 * 1024 * 1024)
                {
                    var archived = Path.Combine(LogDir, $"client_{DateTime.Now:yyyyMMddHHmmss}.log");
                    File.Move(LogFile, archived, overwrite: true);
                }
            }
            catch
            {
                // Logging must never crash the app.
            }
        }

        LineWritten?.Invoke(line);
    }
}
