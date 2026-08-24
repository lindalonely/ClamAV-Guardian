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
    public bool RealTimeProtectionEnabled { get; set; } = true;
    public bool AutoQuarantineOnDetection { get; set; } = true;

    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = true;

    /// <summary>
    /// One-time migration marker: false for any settings.json written before protection
    /// defaults changed to "on". Letting a fresh AppSettings() also default to true keeps
    /// this consistent for brand-new installs, while existing installs get upgraded to the
    /// same secure defaults exactly once — after that, whatever the user chooses sticks.
    /// </summary>
    public bool SecureDefaultsApplied { get; set; } = false;

    public List<string> ScanExclusionPaths { get; set; } = new();
    public List<string> ScanExclusionExtensions { get; set; } = new();

    public int UpdateCheckIntervalHours { get; set; } = 2;
    public string LastUpdateTimeUtc { get; set; } = string.Empty;

    public bool ShowNotifications { get; set; } = true;

    public PostScanAction AfterScanAction { get; set; } = PostScanAction.None;

    public static string DefaultQuarantinePath =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
            "ClamAVGuardian", "Quarantine");
}
