using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using ClamAVGuardian.Ipc;
using ClamAVGuardian.Services;

namespace ClamAVGuardian.Service;

/// <summary>
/// Checks GitHub Releases for a newer ClamAV Guardian build and applies it silently via
/// `msiexec /quiet` — safe here specifically because this process already runs as SYSTEM,
/// so no UAC prompt is ever involved, unlike every prior manual install/upgrade this
/// project needed a human to click through.
/// </summary>
public static class SelfUpdateService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/lindalonely/ClamAV-Guardian/releases/latest";

    private static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClamAVGuardian");
    private static readonly string PendingMarkerPath = Path.Combine(StateDir, "pending-update.marker");
    private static readonly string MsiexecLogPath = Path.Combine(StateDir, "update-msiexec.log");

    public static event Action<DownloadProgress>? ProgressChanged;

    /// <summary>
    /// Called once at service startup, before anything else touches the pending marker.
    /// If the previous run left one behind, the update that wrote it never actually took
    /// effect by the time this (older or same) version is running again — the most common
    /// cause is Windows Smart App Control (or an antivirus) silently blocking the newly
    /// installed exe/service from running. Logs a clear, actionable message either way and
    /// always clears the marker so a genuine one-off failure doesn't nag forever.
    /// </summary>
    public static void ReconcilePendingUpdate()
    {
        try
        {
            if (!File.Exists(PendingMarkerPath)) return;

            var lines = File.ReadAllLines(PendingMarkerPath);
            var targetVersionText = lines.Length > 0 ? lines[0] : null;
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

            if (Version.TryParse(targetVersionText, out var targetVersion) && targetVersion <= currentVersion)
            {
                AppLogger.Info($"Self-update to v{targetVersion} completed successfully.");
            }
            else
            {
                AppLogger.Error(
                    $"Self-update to v{targetVersionText} did not take effect — still running v{currentVersion}. " +
                    "This is usually Windows Smart App Control (or an antivirus) blocking the newly installed, " +
                    $"unsigned executable from running. See '{MsiexecLogPath}' for the msiexec install log, " +
                    "or check Windows Security > App & browser control > Smart App Control for a block notification.");
            }

            File.Delete(PendingMarkerPath);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to reconcile pending self-update state", ex);
        }
    }

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClamAVGuardian-SelfUpdater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static async Task<AppUpdateCheckResult> CheckForUpdateAsync()
    {
        try
        {
            var release = await FetchLatestReleaseAsync();
            if (release == null) return new AppUpdateCheckResult { UpdateAvailable = false };

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

            if (!Version.TryParse(release.Value.Version, out var latestVersion) || latestVersion <= currentVersion)
            {
                return new AppUpdateCheckResult { UpdateAvailable = false, LatestVersion = release.Value.Version };
            }

            AppLogger.Info($"Update available: v{release.Value.Version} (current: v{currentVersion}).");
            return new AppUpdateCheckResult
            {
                UpdateAvailable = true,
                LatestVersion = release.Value.Version,
                ReleaseNotesUrl = release.Value.HtmlUrl,
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update check failed", ex);
            return new AppUpdateCheckResult { UpdateAvailable = false };
        }
    }

    public static async Task ApplyPendingUpdateAsync()
    {
        string? msiPath = null;
        var currentVersion = (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0)).ToString(4);
        string? targetVersion = null;
        try
        {
            Report(DownloadStage.Checking, "Checking for the latest release...", currentVersion, null);
            var release = await FetchLatestReleaseAsync();
            if (release == null || release.Value.MsiUrl == null)
            {
                AppLogger.Warn("No MSI asset found in latest release; cannot self-update.");
                Report(DownloadStage.Failed, "No update package found.", currentVersion, null);
                return;
            }

            targetVersion = release.Value.Version;

            var tempDir = Path.Combine(Path.GetTempPath(), "ClamAVGuardianUpdate");
            Directory.CreateDirectory(tempDir);
            msiPath = Path.Combine(tempDir, release.Value.MsiName!);

            AppLogger.Info($"Downloading update from '{release.Value.MsiUrl}'.");
            Report(DownloadStage.Downloading, $"Downloading v{targetVersion}...", currentVersion, targetVersion);
            await DownloadWithProgressAsync(release.Value.MsiUrl, msiPath, currentVersion, targetVersion);

            if (release.Value.ChecksumUrl != null)
            {
                Report(DownloadStage.Verifying, "Verifying download...", currentVersion, targetVersion);
                var expectedChecksumText = await Http.GetStringAsync(release.Value.ChecksumUrl);
                var expectedHash = expectedChecksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim().ToLowerInvariant();

                using var sha256 = SHA256.Create();
                await using var stream = File.OpenRead(msiPath);
                var actualHash = Convert.ToHexString(await sha256.ComputeHashAsync(stream)).ToLowerInvariant();

                if (string.IsNullOrEmpty(expectedHash) || actualHash != expectedHash)
                {
                    AppLogger.Error($"Update checksum mismatch (expected '{expectedHash}', got '{actualHash}'). Aborting self-update — will not install an unverified download.");
                    Report(DownloadStage.Failed, "Checksum verification failed; update aborted.", currentVersion, targetVersion);
                    TryDeleteFile(msiPath);
                    return;
                }

                AppLogger.Info("Update checksum verified.");
            }
            else
            {
                AppLogger.Warn("No checksum published for this release; installing without verification.");
            }

            AppLogger.Info("Applying update via msiexec (silent, no prompt — this process already runs as SYSTEM).");
            Report(DownloadStage.Installing, $"Installing v{targetVersion}...", currentVersion, targetVersion);

            // Recorded before launching msiexec (which is about to stop this very service) so
            // that whichever version starts up next can tell whether this update actually took
            // effect — see ReconcilePendingUpdate. Deliberately NOT waiting on the msiexec
            // process here: its own install steps stop this service via SCM, and this method
            // runs on the same task BackgroundService.StopAsync waits on to shut down, so
            // blocking here until msiexec exits would deadlock against the stop it's waiting for.
            Directory.CreateDirectory(StateDir);
            File.WriteAllText(PendingMarkerPath, targetVersion + Environment.NewLine + DateTime.UtcNow.ToString("O"));

            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{msiPath}\" /quiet /norestart /l*v \"{MsiexecLogPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
            Report(DownloadStage.Done, $"Update to v{targetVersion} applied. The service will restart shortly.", currentVersion, targetVersion);

            // The MSI's ServiceControl entries will stop this very service, replace its
            // files, and restart it — nothing further to do from inside this process.
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to apply update", ex);
            Report(DownloadStage.Failed, $"Update failed: {ex.Message}", currentVersion, targetVersion);
            if (msiPath != null) TryDeleteFile(msiPath);
        }
    }

    private static async Task DownloadWithProgressAsync(string url, string destinationPath, string currentVersion, string targetVersion)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var httpStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(destinationPath);

        var buffer = new byte[81920];
        long bytesReceived = 0;
        var lastReport = DateTime.MinValue;
        int read;
        while ((read = await httpStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesReceived += read;

            var now = DateTime.UtcNow;
            if (now - lastReport > TimeSpan.FromMilliseconds(200))
            {
                lastReport = now;
                Report(DownloadStage.Downloading, $"Downloading v{targetVersion}...", currentVersion, targetVersion, bytesReceived, totalBytes);
            }
        }

        Report(DownloadStage.Downloading, $"Downloading v{targetVersion}...", currentVersion, targetVersion, bytesReceived, totalBytes);
    }

    private static void Report(DownloadStage stage, string message, string? currentVersion, string? targetVersion, long bytesReceived = 0, long totalBytes = 0)
    {
        ProgressChanged?.Invoke(new DownloadProgress
        {
            Stage = stage,
            Message = message,
            CurrentVersion = currentVersion,
            TargetVersion = targetVersion,
            BytesReceived = bytesReceived,
            TotalBytes = totalBytes,
        });
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* best effort cleanup */ }
    }

    private readonly record struct ReleaseInfo(string Version, string? HtmlUrl, string? MsiUrl, string? MsiName, string? ChecksumUrl);

    private static async Task<ReleaseInfo?> FetchLatestReleaseAsync()
    {
        using var response = await Http.GetAsync(ReleasesApiUrl);
        if (!response.IsSuccessStatusCode)
        {
            AppLogger.Warn($"Update check request failed: HTTP {(int)response.StatusCode}.");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        var version = tagName.TrimStart('v', 'V');
        var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;

        string? msiUrl = null, msiName = null, checksumUrl = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var downloadUrl = asset.TryGetProperty("browser_download_url", out var dlEl) ? dlEl.GetString() : null;

                if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    msiUrl = downloadUrl;
                    msiName = name;
                }
                else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                {
                    checksumUrl = downloadUrl;
                }
            }
        }

        return new ReleaseInfo(version, htmlUrl, msiUrl, msiName, checksumUrl);
    }
}
