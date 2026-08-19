using ClamAVGuardian.Models;

namespace ClamAVGuardian.Ipc;

public class ScanRequest
{
    public ScanKind Kind { get; set; }
    public string? CustomPath { get; set; }
}

public class UpdateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AppUpdateCheckResult
{
    public bool UpdateAvailable { get; set; }
    public string? LatestVersion { get; set; }
    public string? ReleaseNotesUrl { get; set; }
}

public class InstallClamAvResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public enum DownloadStage
{
    Checking,
    Downloading,
    Verifying,
    Installing,
    Done,
    Failed,
}

public class DownloadProgress
{
    public DownloadStage Stage { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? CurrentVersion { get; set; }
    public string? TargetVersion { get; set; }
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }

    public int PercentComplete => TotalBytes > 0 ? (int)(BytesReceived * 100 / TotalBytes) : 0;
}
