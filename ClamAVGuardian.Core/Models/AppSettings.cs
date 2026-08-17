using System.Collections.Generic;

namespace ClamAVGuardian.Models;

public enum PostScanAction
{
    None,
    Shutdown,
    Restart,
    Sleep,
}

public class AppSettings
{
    public string ClamAvInstallPath { get; set; } = string.Empty;
    public string QuarantinePath { get; set; } = string.Empty;

    public List<string> RealTimeWatchedFolders { get; set; } = new();
    public bool RealTimeProtectionEnabled { get; set; } = false;
    public bool AutoQuarantineOnDetection { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;

    public List<string> ScanExclusionPaths { get; set; } = new();
    public List<string> ScanExclusionExtensions { get; set; } = new();

    public int UpdateCheckIntervalHours { get; set; } = 2;
    public string LastUpdateTimeUtc { get; set; } = string.Empty;

    public bool ShowNotifications { get; set; } = true;

    public PostScanAction AfterScanAction { get; set; } = PostScanAction.None;
    public bool DesktopShortcutCreated { get; set; } = false;

    public static string DefaultQuarantinePath =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
            "ClamAVGuardian", "Quarantine");
}
