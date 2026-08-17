namespace ClamAVGuardian.Models;

public class QuarantineEntry
{
    public required string Id { get; init; }
    public required string OriginalPath { get; init; }
    public required string QuarantinedFilePath { get; init; }
    public required string ThreatName { get; init; }
    public DateTime QuarantinedAtUtc { get; init; } = DateTime.UtcNow;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
