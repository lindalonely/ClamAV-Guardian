using System;
using System.IO;
using ClamAVGuardian.Models;
using Microsoft.Win32;

namespace ClamAVGuardian.Services;

public static class ClamAvLocator
{
    private static readonly string[] CommonPaths =
    {
        @"C:\Program Files\ClamAV",
        @"C:\Program Files (x86)\ClamAV",
        @"C:\ClamAV",
    };

    public static ClamAvInstallation? TryLocate(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var candidate = new ClamAvInstallation { InstallDir = configuredPath };
            if (candidate.IsValid) return candidate;
        }

        var fromRegistry = TryRegistry();
        if (fromRegistry != null) return fromRegistry;

        foreach (var path in CommonPaths)
        {
            var candidate = new ClamAvInstallation { InstallDir = path };
            if (candidate.IsValid) return candidate;
        }

        var fromPath = TryEnvironmentPath();
        if (fromPath != null) return fromPath;

        return null;
    }

    private static ClamAvInstallation? TryRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ClamAV");
            var installDir = key?.GetValue("InstallDir") as string
                              ?? key?.GetValue("Path") as string;
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                var candidate = new ClamAvInstallation { InstallDir = installDir };
                if (candidate.IsValid) return candidate;
            }
        }
        catch
        {
            // Registry key absent or inaccessible; fall through to other strategies.
        }

        return null;
    }

    private static ClamAvInstallation? TryEnvironmentPath()
    {
        try
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = new ClamAvInstallation { InstallDir = dir.Trim() };
                if (candidate.IsValid) return candidate;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
