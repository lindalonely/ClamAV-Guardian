using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using ClamAVGuardian.Models;
using Timer = System.Timers.Timer;

namespace ClamAVGuardian.Services;

public class RealTimeProtectionService : IDisposable
{
    private readonly ClamAvInstallation _install;
    private readonly ClamdClient _clamdClient = new();
    private readonly QuarantineService _quarantineService;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _debounceTimer;
    private volatile bool _usingClamd;

    /// <summary>
    /// Caps how many "clamscan" fallback processes can run at once. Without this, a burst of
    /// file events (extracting an archive, copying a folder) fires one clamscan process per
    /// file with no limit — each loading its own full copy of the virus database — and can
    /// exhaust CPU/memory. Only guards the fallback path; clamd handles its own concurrency
    /// internally and doesn't need this (it's one resident process, not one per scan).
    /// </summary>
    private readonly SemaphoreSlim _clamscanFallbackLimiter = new(1, 1);

    public bool IsRunning { get; private set; }
    public bool AutoQuarantine { get; set; } = true;
    public string EngineDescription => _usingClamd ? "clamd (fast)" : "clamscan (fallback)";

    public event Action<ScanItem, bool>? ThreatDetected;
    public event Action<string>? FileScanned;
    public event Action<string>? StatusMessage;

    public RealTimeProtectionService(ClamAvInstallation install, QuarantineService quarantineService)
    {
        _install = install;
        _quarantineService = quarantineService;
    }

    public async Task StartAsync(IEnumerable<string> folders)
    {
        Stop();

        _usingClamd = await _clamdClient.IsAvailableAsync();
        StatusMessage?.Invoke($"Real-time protection engine: {EngineDescription}");

        foreach (var folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };
                watcher.Created += OnFsEvent;
                watcher.Changed += OnFsEvent;
                watcher.Renamed += OnFsEvent;
                watcher.Error += (_, e) => AppLogger.Error("FileSystemWatcher error", e.GetException());

                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to watch folder '{folder}'", ex);
            }
        }

        _debounceTimer = new Timer(500) { AutoReset = true };
        _debounceTimer.Elapsed += ProcessPendingQueue;
        _debounceTimer.Start();

        IsRunning = true;
    }

    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { /* ignore */ }
        }
        _watchers.Clear();

        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        _pending.Clear();
        IsRunning = false;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath)) return;
        _pending[e.FullPath] = DateTime.UtcNow;
    }

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(1200);

    private void ProcessPendingQueue(object? sender, ElapsedEventArgs e)
    {
        var now = DateTime.UtcNow;
        var ready = _pending.Where(kv => now - kv.Value >= DebounceWindow).Select(kv => kv.Key).ToList();

        foreach (var path in ready)
        {
            _pending.TryRemove(path, out _);
            _ = ScanSingleFileAsync(path);
        }
    }

    private async Task ScanSingleFileAsync(string path)
    {
        if (!await WaitUntilReadableAsync(path)) return;

        try
        {
            ScanItem item;

            if (_usingClamd)
            {
                var result = await _clamdClient.ScanFileAsync(path);
                if (!result.Success)
                {
                    // clamd dropped or errored; fall back for this file.
                    item = await RunClamscanSingleFileThrottledAsync(path);
                }
                else
                {
                    item = result.Infected
                        ? new ScanItem { Path = path, Status = ScanStatus.Infected, ThreatName = result.ThreatName }
                        : new ScanItem { Path = path, Status = ScanStatus.Clean };
                }
            }
            else
            {
                item = await RunClamscanSingleFileThrottledAsync(path);
            }

            FileScanned?.Invoke(path);

            if (item.Status == ScanStatus.Infected)
            {
                var quarantined = false;
                if (AutoQuarantine)
                {
                    var entry = _quarantineService.QuarantineFile(path, item.ThreatName ?? "Unknown");
                    quarantined = entry != null;
                }
                ThreatDetected?.Invoke(item, quarantined);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Real-time scan failed for '{path}'", ex);
        }
    }

    private static async Task<bool> WaitUntilReadableAsync(string path)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return true;
            }
            catch (IOException)
            {
                await Task.Delay(300);
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    private async Task<ScanItem> RunClamscanSingleFileThrottledAsync(string path)
    {
        await _clamscanFallbackLimiter.WaitAsync();
        try
        {
            return await RunClamscanSingleFileAsync(path);
        }
        finally
        {
            _clamscanFallbackLimiter.Release();
        }
    }

    private async Task<ScanItem> RunClamscanSingleFileAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _install.ClamScanPath,
                Arguments = $"--stdout --no-summary \"{path}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return new ScanItem { Path = path, Status = ScanStatus.Error, ErrorMessage = "Failed to start clamscan" };

            var output = (await process.StandardOutput.ReadToEndAsync()).Trim();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }

            var match = Regex.Match(output, @"^(?<path>.+): (?<status>.+)$");
            if (!match.Success) return new ScanItem { Path = path, Status = ScanStatus.Clean };

            var status = match.Groups["status"].Value;
            if (status.EndsWith("FOUND", StringComparison.Ordinal))
            {
                return new ScanItem { Path = path, Status = ScanStatus.Infected, ThreatName = status[..^"FOUND".Length].Trim() };
            }

            return new ScanItem { Path = path, Status = ScanStatus.Clean };
        }
        catch (Exception ex)
        {
            return new ScanItem { Path = path, Status = ScanStatus.Error, ErrorMessage = ex.Message };
        }
    }

    public void Dispose()
    {
        Stop();
        _clamscanFallbackLimiter.Dispose();
    }
}
