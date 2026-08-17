using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClamAVGuardian.Models;

namespace ClamAVGuardian.Services;

public class ScanService
{
    private readonly ClamAvInstallation _install;

    public event Action<ScanItem>? ItemScanned;
    public event Action<string>? StatusMessage;

    public ScanService(ClamAvInstallation install)
    {
        _install = install;
    }

    public static List<string> BuildQuickScanTargets()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads",
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.GetTempPath(),
        };

        return candidates.Where(Directory.Exists).Distinct().ToList();
    }

    public static List<string> BuildFullScanTargets()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName)
            .ToList();
    }

    public async Task<ScanSummary> RunScanAsync(
        IEnumerable<string> targets,
        IEnumerable<string>? excludePaths,
        IEnumerable<string>? excludeExtensions,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var summary = new ScanSummary();

        var targetList = targets.Where(Directory.Exists).ToList();
        if (targetList.Count == 0)
        {
            StatusMessage?.Invoke("No valid scan targets found.");
            return summary;
        }

        if (!_install.HasDatabaseFiles)
        {
            summary.DatabaseMissing = true;
            StatusMessage?.Invoke("No virus database found. Click 'Update Now' on the Updates page first.");
            return summary;
        }

        var args = new StringBuilder();
        args.Append("--recursive --stdout --no-summary ");

        foreach (var ext in excludeExtensions ?? Enumerable.Empty<string>())
        {
            var pattern = Regex.Escape(ext.TrimStart('.'));
            args.Append($"--exclude=\"\\.{pattern}$\" ");
        }

        foreach (var path in excludePaths ?? Enumerable.Empty<string>())
        {
            var pattern = Regex.Escape(path);
            args.Append($"--exclude-dir=\"{pattern}\" ");
        }

        foreach (var target in targetList)
        {
            args.Append($"\"{target}\" ");
        }

        var psi = new ProcessStartInfo
        {
            FileName = _install.ClamScanPath,
            Arguments = args.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var lineRegex = new Regex(@"^(?<path>.+): (?<status>.+)$", RegexOptions.Compiled);

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var item = ParseLine(e.Data, lineRegex);
            if (item == null) return;

            if (item.Status == ScanStatus.Infected) summary.InfectedFound++;
            else if (item.Status == ScanStatus.Error) summary.Errors++;
            summary.FilesScanned++;

            ItemScanned?.Invoke(item);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) StatusMessage?.Invoke(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
            });

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            summary.WasCancelled = true;
        }
        finally
        {
            sw.Stop();
            summary.Duration = sw.Elapsed;
        }

        return summary;
    }

    private static ScanItem? ParseLine(string line, Regex lineRegex)
    {
        var match = lineRegex.Match(line);
        if (!match.Success) return null;

        var path = match.Groups["path"].Value;
        var status = match.Groups["status"].Value;

        if (status.EndsWith("OK", StringComparison.Ordinal))
        {
            return new ScanItem { Path = path, Status = ScanStatus.Clean };
        }

        if (status.EndsWith("FOUND", StringComparison.Ordinal))
        {
            var threatName = status[..^"FOUND".Length].Trim();
            return new ScanItem { Path = path, Status = ScanStatus.Infected, ThreatName = threatName };
        }

        return new ScanItem { Path = path, Status = ScanStatus.Error, ErrorMessage = status };
    }
}
