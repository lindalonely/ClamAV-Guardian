using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClamAVGuardian.Services;

namespace ClamAVGuardian.Service;

/// <summary>
/// Downloads and silently installs the official ClamAV Windows engine (clamscan, clamd,
/// freshclam) when it isn't found on the machine, so ClamAV Guardian works out of the box
/// without a separate manual install first.
///
/// Unlike our own self-update (where we control the release pipeline and publish a SHA-256
/// checksum to verify against), ClamAV is a third-party upstream that doesn't publish a
/// simple checksum sidecar for its Windows MSI. Integrity here relies on HTTPS transport
/// security plus only ever downloading from the official clamav.net domain — weaker than a
/// pinned checksum, but standard practice for fetching third-party software whose release
/// process we don't control.
/// </summary>
public static class ClamAvInstallerService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Cisco-Talos/clamav/releases/latest";
    private const string DownloadUrlTemplate = "https://www.clamav.net/downloads/production/clamav-{0}.win.x64.msi";
    private const long MinimumPlausibleInstallerBytes = 50_000_000; // real installer is ~200MB; guards against a truncated/error-page download

    public static event Action<string>? StatusChanged;

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClamAVGuardian-Installer");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static async Task<(bool Success, string Message)> DownloadAndInstallAsync()
    {
        string? msiPath = null;
        try
        {
            Report("Checking the latest ClamAV version...");
            var version = await GetLatestVersionAsync();
            if (version == null)
            {
                return (false, "Could not determine the latest ClamAV version.");
            }

            var downloadUrl = string.Format(DownloadUrlTemplate, version);
            var tempDir = Path.Combine(Path.GetTempPath(), "ClamAVGuardianClamAvInstall");
            Directory.CreateDirectory(tempDir);
            msiPath = Path.Combine(tempDir, $"clamav-{version}.win.x64.msi");

            Report($"Downloading ClamAV {version}...");
            AppLogger.Info($"Downloading ClamAV installer from '{downloadUrl}'.");

            using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"Download failed: HTTP {(int)response.StatusCode}.");
                }

                await using var fileStream = File.Create(msiPath);
                await response.Content.CopyToAsync(fileStream);
            }

            var fileInfo = new FileInfo(msiPath);
            if (fileInfo.Length < MinimumPlausibleInstallerBytes)
            {
                AppLogger.Error($"Downloaded ClamAV installer was only {fileInfo.Length} bytes — too small to be genuine; aborting.");
                return (false, "Downloaded file looked too small to be a valid installer; aborting.");
            }

            Report("Installing ClamAV (this can take a minute)...");
            AppLogger.Info($"Installing ClamAV silently from '{msiPath}'.");

            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{msiPath}\" /quiet /norestart",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "Failed to start the ClamAV installer process.");
            }

            await process.WaitForExitAsync();

            // 3010 = success, reboot required (harmless here — ClamAV doesn't need one to work).
            if (process.ExitCode != 0 && process.ExitCode != 3010)
            {
                return (false, $"ClamAV installer exited with code {process.ExitCode}.");
            }

            Report("ClamAV installed.");
            AppLogger.Info($"ClamAV {version} installed successfully.");
            return (true, $"ClamAV {version} installed successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to auto-install ClamAV", ex);
            return (false, $"Failed to install ClamAV: {ex.Message}");
        }
        finally
        {
            if (msiPath != null) TryDeleteFile(msiPath);
        }
    }

    private static async Task<string?> GetLatestVersionAsync()
    {
        using var response = await Http.GetAsync(LatestReleaseApiUrl);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var tagName = doc.RootElement.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        if (string.IsNullOrEmpty(tagName)) return null;

        // Tags look like "clamav-1.5.4".
        const string prefix = "clamav-";
        return tagName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? tagName[prefix.Length..] : tagName;
    }

    private static void Report(string message)
    {
        AppLogger.Info(message);
        StatusChanged?.Invoke(message);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* best effort cleanup */ }
    }
}
