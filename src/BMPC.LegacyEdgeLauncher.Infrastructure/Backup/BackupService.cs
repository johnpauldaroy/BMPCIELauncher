using System.Security.Cryptography;
using System.Text.Json;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Results;
using BMPC.LegacyEdgeLauncher.Infrastructure.Registry;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Backup;

/// <summary>
/// Creates and restores backup sets (section 21): the SQLite database, the published
/// site list, and a manifest with SHA-256 hashes. Each set lives in its own folder under
/// C:\ProgramData\BMPC\LegacyEdgeLauncher\Backups.
/// </summary>
public class BackupService : IBackupService
{
    private const string ManifestFileName = "manifest.json";

    private static readonly IReadOnlySet<string> AllowedBackupFiles =
        new HashSet<string>(StringComparer.Ordinal) { "launcher.db", "sitelist.xml" };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<BackupService> _logger;

    public BackupService(ILogger<BackupService> logger)
    {
        _logger = logger;
    }

    private sealed record Manifest(
        Guid Id,
        DateTimeOffset CreatedAt,
        string Reason,
        string CreatedBy,
        Dictionary<string, string> FileHashes);

    public async Task<BackupResult> CreateAsync(string reason, CancellationToken ct)
    {
        try
        {
            var id = Guid.NewGuid();
            var folder = Path.Combine(AppConstants.BackupDirectory,
                $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}_{id:N}");
            Directory.CreateDirectory(folder);

            var hashes = new Dictionary<string, string>();

            if (File.Exists(AppConstants.DatabasePath))
            {
                var dbCopy = Path.Combine(folder, "launcher.db");
                // VACUUM INTO produces a consistent copy even while other connections are open.
                await using (var source = new SqliteConnection($"Data Source={AppConstants.DatabasePath}"))
                {
                    await source.OpenAsync(ct);
                    await using var cmd = source.CreateCommand();
                    cmd.CommandText = "VACUUM INTO @path";
                    cmd.Parameters.AddWithValue("@path", dbCopy);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                hashes["launcher.db"] = await Sha256OfFileAsync(dbCopy, ct);
            }

            if (File.Exists(AppConstants.SiteListFilePath))
            {
                var xmlCopy = Path.Combine(folder, "sitelist.xml");
                File.Copy(AppConstants.SiteListFilePath, xmlCopy);
                hashes["sitelist.xml"] = await Sha256OfFileAsync(xmlCopy, ct);
            }

            if (hashes.Count == 0)
            {
                Directory.Delete(folder, recursive: true);
                return new BackupResult(false, "There is nothing to back up yet — no database or site list exists.");
            }

            var manifest = new Manifest(id, DateTimeOffset.Now, reason, Environment.UserName, hashes);
            await File.WriteAllTextAsync(Path.Combine(folder, ManifestFileName),
                JsonSerializer.Serialize(manifest, JsonOptions), ct);

            _logger.LogInformation("Backup {Id} created at {Folder} ({Reason})", id, folder, reason);
            return new BackupResult(true, $"Backup created ({hashes.Count} files).", id, folder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed");
            return new BackupResult(false, "Backup failed.", null, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken ct)
    {
        var list = new List<BackupInfo>();
        if (!Directory.Exists(AppConstants.BackupDirectory))
            return list;

        foreach (var folder in Directory.GetDirectories(AppConstants.BackupDirectory))
        {
            if (IsReparsePoint(folder))
            {
                _logger.LogWarning("Skipping backup reparse point: {Folder}", folder);
                continue;
            }

            var manifestPath = Path.Combine(folder, ManifestFileName);
            if (!File.Exists(manifestPath) || IsReparsePoint(manifestPath)) continue;
            try
            {
                var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath, ct));
                if (manifest is null || !IsValidManifest(manifest))
                {
                    _logger.LogWarning("Skipping backup folder with an invalid manifest: {Folder}", folder);
                    continue;
                }
                var combinedHash = string.Join(",", manifest.FileHashes.OrderBy(k => k.Key).Select(k => k.Value));
                list.Add(new BackupInfo(manifest.Id, manifest.CreatedAt, manifest.Reason,
                    manifest.CreatedBy, folder, combinedHash));
            }
            catch (JsonException)
            {
                _logger.LogWarning("Skipping backup folder with unreadable manifest: {Folder}", folder);
            }
        }

        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    public async Task<RestoreResult> RestoreAsync(Guid backupId, CancellationToken ct)
    {
        var backups = await ListAsync(ct);
        var backup = backups.FirstOrDefault(b => b.Id == backupId);
        if (backup is null)
            return new RestoreResult(false, "Backup not found.");

        try
        {
            var manifestPath = Path.Combine(backup.Path, ManifestFileName);
            if (IsReparsePoint(backup.Path) || IsReparsePoint(manifestPath))
                return new RestoreResult(false, "The backup contains a reparse point and was not restored.");

            var manifest = JsonSerializer.Deserialize<Manifest>(
                await File.ReadAllTextAsync(manifestPath, ct))!;

            if (!IsValidManifest(manifest))
                return new RestoreResult(false, "The backup manifest is invalid and was not restored.");

            // Verify integrity before touching anything.
            foreach (var (file, expectedHash) in manifest.FileHashes)
            {
                var path = BackupFilePath(backup.Path, file);
                if (!File.Exists(path) || IsReparsePoint(path))
                    return new RestoreResult(false, $"Backup file '{file}' is missing. The backup set is incomplete.");
                var actual = await Sha256OfFileAsync(path, ct);
                if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                    return new RestoreResult(false, $"Backup file '{file}' failed its integrity check and was not restored.");
            }

            var edgeRestart = false;

            if (manifest.FileHashes.ContainsKey("launcher.db"))
            {
                // Release pooled SQLite handles so the database file can be replaced.
                SqliteConnection.ClearAllPools();
                Directory.CreateDirectory(AppConstants.DatabaseDirectory);
                File.Copy(BackupFilePath(backup.Path, "launcher.db"), AppConstants.DatabasePath, overwrite: true);
            }

            if (manifest.FileHashes.ContainsKey("sitelist.xml"))
            {
                Directory.CreateDirectory(AppConstants.SiteListDirectory);
                File.Copy(BackupFilePath(backup.Path, "sitelist.xml"), AppConstants.SiteListFilePath, overwrite: true);
                RestartFlag.Set();
                edgeRestart = true;
            }

            _logger.LogWarning("Backup {Id} from {CreatedAt} was restored", backup.Id, backup.CreatedAt);
            return new RestoreResult(true,
                $"Backup from {backup.CreatedAt:g} restored successfully.", edgeRestart);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore of backup {Id} failed", backupId);
            return new RestoreResult(false, "Restore failed.", false, ex.Message);
        }
    }

    private static async Task<string> Sha256OfFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static bool IsValidManifest(Manifest manifest) =>
        manifest.Id != Guid.Empty &&
        manifest.FileHashes.Count is > 0 and <= 2 &&
        manifest.FileHashes.All(pair =>
            AllowedBackupFiles.Contains(pair.Key) &&
            pair.Value.Length == 64 &&
            pair.Value.All(Uri.IsHexDigit));

    private static string BackupFilePath(string backupFolder, string fileName)
    {
        if (!AllowedBackupFiles.Contains(fileName))
            throw new InvalidDataException("The backup manifest contains an unsupported file name.");

        var canonicalFolder = Path.GetFullPath(backupFolder).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(canonicalFolder, fileName));
        if (!string.Equals(Path.GetDirectoryName(path), canonicalFolder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The backup file path escapes its backup directory.");

        return path;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
