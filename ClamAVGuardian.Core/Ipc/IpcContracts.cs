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
