using System.IO;

namespace ClamAVGuardian.Models;

public class ClamAvInstallation
{
    public required string InstallDir { get; init; }
    public string ClamScanPath => Path.Combine(InstallDir, "clamscan.exe");
    public string ClamdScanPath => Path.Combine(InstallDir, "clamdscan.exe");
    public string FreshClamPath => Path.Combine(InstallDir, "freshclam.exe");
    public string ClamdPath => Path.Combine(InstallDir, "clamd.exe");
    public string FreshClamConfPath => Path.Combine(InstallDir, "freshclam.conf");
    public string ClamdConfPath => Path.Combine(InstallDir, "clamd.conf");
    public string FreshClamConfSamplePath => Path.Combine(InstallDir, "conf_examples", "freshclam.conf.sample");
    public string ClamdConfSamplePath => Path.Combine(InstallDir, "conf_examples", "clamd.conf.sample");
    public string ClamdLogPath => Path.Combine(InstallDir, "clamd.log");
    public string ClamdPidPath => Path.Combine(InstallDir, "clamd.pid");
    public string DatabaseDir => Path.Combine(InstallDir, "database");

    public bool HasDatabaseFiles =>
        Directory.Exists(DatabaseDir) &&
        (Directory.GetFiles(DatabaseDir, "*.cvd").Length > 0 || Directory.GetFiles(DatabaseDir, "*.cld").Length > 0);

    public bool IsValid => File.Exists(ClamScanPath) && File.Exists(FreshClamPath);
}
