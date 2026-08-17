using System;

namespace ClamAVGuardian.Services;

/// <summary>
/// Registers the client to start at login. Now that the client is unelevated (all the
/// privileged work moved into ClamAVGuardian.Service, which auto-starts via SCM
/// regardless of this), a plain HKCU Run key works correctly — the scheduled-task
/// workaround from the single-elevated-app era was only needed to get an admin-required
/// process to auto-elevate silently, which no longer applies here.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ClamAVGuardian";

    public static bool Enable(string exePath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.SetValue(RunValueName, $"\"{exePath}\"");
            return true;
        }
        catch (Exception ex)
        {
            ClientLogger.Error("Failed to register startup Run key entry", ex);
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            ClientLogger.Error("Failed to remove startup Run key entry", ex);
            return false;
        }
    }
}
