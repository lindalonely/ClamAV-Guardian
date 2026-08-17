using System;

namespace ClamAVGuardian.Models;

public class ScanSummary
{
    public int FilesScanned { get; set; }
    public int InfectedFound { get; set; }
    public int Errors { get; set; }
    public TimeSpan Duration { get; set; }
    public bool WasCancelled { get; set; }
    public bool DatabaseMissing { get; set; }
}
