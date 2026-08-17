using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using ClamAVGuardian.Models;

namespace ClamAVGuardian.Services;

/// <summary>
/// Quarantine neutralizes files with a reversible single-byte XOR so they cannot be
/// accidentally executed or re-flagged in place; it is not meant as strong encryption.
/// </summary>
public class QuarantineService
{
    private const byte XorKey = 0x5A;
    private readonly string _quarantineDir;
    private readonly string _manifestPath;
    private readonly object _lock = new();

    public QuarantineService(string quarantineDir)
    {
        _quarantineDir = quarantineDir;
        _manifestPath = Path.Combine(_quarantineDir, "manifest.json");
        Directory.CreateDirectory(_quarantineDir);
    }

    public List<QuarantineEntry> LoadEntries()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_manifestPath)) return new List<QuarantineEntry>();
                var json = File.ReadAllText(_manifestPath);
                return JsonSerializer.Deserialize<List<QuarantineEntry>>(json) ?? new List<QuarantineEntry>();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load quarantine manifest", ex);
                return new List<QuarantineEntry>();
            }
        }
    }

    private void SaveEntries(List<QuarantineEntry> entries)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_manifestPath, json);
        }
    }

    public QuarantineEntry? QuarantineFile(string originalPath, string threatName)
    {
        try
        {
            if (!File.Exists(originalPath)) return null;

            var id = Guid.NewGuid().ToString("N");
            var quarantinedFilePath = Path.Combine(_quarantineDir, id + ".quar");
            var fileInfo = new FileInfo(originalPath);
            var sizeBytes = fileInfo.Length;
            var sha256 = ComputeSha256(originalPath);

            XorCopy(originalPath, quarantinedFilePath);
            File.Delete(originalPath);

            var entry = new QuarantineEntry
            {
                Id = id,
                OriginalPath = originalPath,
                QuarantinedFilePath = quarantinedFilePath,
                ThreatName = threatName,
                SizeBytes = sizeBytes,
                Sha256 = sha256,
            };

            var entries = LoadEntries();
            entries.Add(entry);
            SaveEntries(entries);

            AppLogger.Info($"Quarantined '{originalPath}' ({threatName})");
            return entry;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to quarantine '{originalPath}'", ex);
            return null;
        }
    }

    public bool Restore(string id)
    {
        var entries = LoadEntries();
        var entry = entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return false;

        try
        {
            var targetDir = Path.GetDirectoryName(entry.OriginalPath);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

            XorCopy(entry.QuarantinedFilePath, entry.OriginalPath);
            File.Delete(entry.QuarantinedFilePath);

            entries.Remove(entry);
            SaveEntries(entries);

            AppLogger.Info($"Restored quarantined file to '{entry.OriginalPath}'");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to restore quarantine entry '{id}'", ex);
            return false;
        }
    }

    public bool DeletePermanently(string id)
    {
        var entries = LoadEntries();
        var entry = entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return false;

        try
        {
            if (File.Exists(entry.QuarantinedFilePath)) File.Delete(entry.QuarantinedFilePath);
            entries.Remove(entry);
            SaveEntries(entries);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to permanently delete quarantine entry '{id}'", ex);
            return false;
        }
    }

    public void DeleteAll()
    {
        foreach (var entry in LoadEntries().ToList())
        {
            DeletePermanently(entry.Id);
        }
    }

    private static void XorCopy(string sourcePath, string destPath)
    {
        const int bufferSize = 81920;
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
        using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        var buffer = new byte[bufferSize];
        int read;
        while ((read = source.Read(buffer, 0, bufferSize)) > 0)
        {
            for (var i = 0; i < read; i++) buffer[i] ^= XorKey;
            dest.Write(buffer, 0, read);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
