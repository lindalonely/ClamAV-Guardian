using System;
using System.IO;
using System.Text.Json;
using ClamAVGuardian.Models;

namespace ClamAVGuardian.Services;

public static class SettingsManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ClamAVGuardian");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        try
        {
            MigrateFromLegacyPerUserLocationIfNeeded();

            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    if (string.IsNullOrWhiteSpace(settings.QuarantinePath))
                        settings.QuarantinePath = AppSettings.DefaultQuarantinePath;
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load settings, using defaults", ex);
        }

        return new AppSettings
        {
            QuarantinePath = AppSettings.DefaultQuarantinePath,
            RealTimeWatchedFolders =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads",
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            }
        };
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save settings", ex);
        }
    }

    /// <summary>
    /// Earlier versions ran as a single elevated desktop app and stored settings per-user
    /// under %AppData%. The service now owns settings machine-wide under %ProgramData%;
    /// on first run, pull forward whatever the most recently used per-user copy had so
    /// existing configuration (ClamAV path, watched folders, etc.) isn't lost.
    /// </summary>
    private static void MigrateFromLegacyPerUserLocationIfNeeded()
    {
        if (File.Exists(SettingsFile)) return;

        try
        {
            const string usersDir = @"C:\Users";
            if (!Directory.Exists(usersDir)) return;

            string? newest = null;
            var newestTime = DateTime.MinValue;

            foreach (var userDir in Directory.GetDirectories(usersDir))
            {
                var candidate = Path.Combine(userDir, "AppData", "Roaming", "ClamAVGuardian", "settings.json");
                if (!File.Exists(candidate)) continue;

                var writeTime = File.GetLastWriteTimeUtc(candidate);
                if (writeTime > newestTime)
                {
                    newestTime = writeTime;
                    newest = candidate;
                }
            }

            if (newest != null)
            {
                Directory.CreateDirectory(SettingsDir);
                File.Copy(newest, SettingsFile);
                AppLogger.Info($"Migrated settings from legacy per-user location '{newest}'.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Settings migration from legacy per-user location failed", ex);
        }
    }
}
