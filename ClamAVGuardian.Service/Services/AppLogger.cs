using System;
using System.IO;

namespace ClamAVGuardian.Services;

public static class AppLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ClamAVGuardian", "logs");

    private static readonly string LogFile = Path.Combine(LogDir, "app.log");
    private static readonly object Lock = new();

    public static string LogFilePath => LogFile;

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
                if (info.Length > 5 * 1024 * 1024)
                {
                    var archived = Path.Combine(LogDir, $"app_{DateTime.Now:yyyyMMddHHmmss}.log");
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

    public static string ReadTail(int maxLines = 500)
    {
        try
        {
            if (!File.Exists(LogFile)) return string.Empty;
            var lines = File.ReadAllLines(LogFile);
            var start = Math.Max(0, lines.Length - maxLines);
            return string.Join(Environment.NewLine, lines[start..]);
        }
        catch
        {
            return string.Empty;
        }
    }
}
