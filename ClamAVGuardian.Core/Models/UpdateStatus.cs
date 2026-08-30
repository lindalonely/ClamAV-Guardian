namespace ClamAVGuardian.Models;

public enum FreshClamServiceState
{
    NotInstalled,
    Stopped,
    Running,
    Unknown
}

public enum ClamdServiceState
{
    NotInstalled,
    Stopped,
    Running,
    Unknown
}

public class UpdateStatus
{
    public FreshClamServiceState ServiceState { get; set; } = FreshClamServiceState.Unknown;
    public string? DatabaseVersion { get; set; }
    public DateTime? LastUpdateUtc { get; set; }
    public bool LastUpdateSucceeded { get; set; }
    public string? LastMessage { get; set; }
}
