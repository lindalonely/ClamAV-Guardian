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
        try
        {
            var release = await FetchLatestReleaseAsync();
            if (release == null || release.Value.MsiUrl == null)
            {
                AppLogger.Warn("No MSI asset found in latest release; cannot self-update.");
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "ClamAVGuardianUpdate");
            Directory.CreateDirectory(tempDir);
            msiPath = Path.Combine(tempDir, release.Value.MsiName!);

            AppLogger.Info($"Downloading update from '{release.Value.MsiUrl}'.");
            var msiBytes = await Http.GetByteArrayAsync(release.Value.MsiUrl);
            await File.WriteAllBytesAsync(msiPath, msiBytes);

            if (release.Value.ChecksumUrl != null)
            {
                var expectedChecksumText = await Http.GetStringAsync(release.Value.ChecksumUrl);
                var expectedHash = expectedChecksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim().ToLowerInvariant();

                using var sha256 = SHA256.Create();
                await using var stream = File.OpenRead(msiPath);
                var actualHash = Convert.ToHexString(await sha256.ComputeHashAsync(stream)).ToLowerInvariant();

                if (string.IsNullOrEmpty(expectedHash) || actualHash != expectedHash)
                {
                    AppLogger.Error($"Update checksum mismatch (expected '{expectedHash}', got '{actualHash}'). Aborting self-update — will not install an unverified download.");
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
            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{msiPath}\" /quiet /norestart",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);

            // The MSI's ServiceControl entries will stop this very service, replace its
            // files, and restart it — nothing further to do from inside this process.
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to apply update", ex);
            if (msiPath != null) TryDeleteFile(msiPath);
        }
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
