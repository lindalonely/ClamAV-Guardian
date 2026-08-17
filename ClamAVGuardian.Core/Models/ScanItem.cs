namespace ClamAVGuardian.Models;

public enum ScanStatus
{
    Clean,
    Infected,
    Error
}

public class ScanItem
{
    public required string Path { get; init; }
    public ScanStatus Status { get; init; }
    public string? ThreatName { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime ScannedAtUtc { get; init; } = DateTime.UtcNow;
    public bool WasQuarantined { get; init; }
}

public enum ScanKind
{
    Quick,
    Full,
    Custom
}
