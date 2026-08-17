using System.Threading.Tasks;
using ClamAVGuardian.Ipc;

namespace ClamAVGuardian.Service;

/// <summary>
/// Checks GitHub Releases for a newer ClamAV Guardian build and applies it silently via
/// `msiexec /quiet` — safe here specifically because this process already runs as SYSTEM,
/// so no UAC prompt is ever involved. Stubbed until the GitHub repo/release pipeline exists.
/// </summary>
public static class SelfUpdateService
{
    public static Task<AppUpdateCheckResult> CheckForUpdateAsync()
    {
        return Task.FromResult(new AppUpdateCheckResult { UpdateAvailable = false });
    }

    public static Task ApplyPendingUpdateAsync()
    {
        return Task.CompletedTask;
    }
}
