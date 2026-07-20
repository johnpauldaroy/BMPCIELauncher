using System.Security.Cryptography;
using System.Text;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using BMPC.LegacyEdgeLauncher.Infrastructure.Registry;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.SiteList;

/// <summary>
/// Publishes a validated site list to the local service directory (section 12).
/// Writes are atomic (temp file + replace), the previous file is kept as a versioned
/// backup, and every publish is recorded with its SHA-256 hash for later verification.
/// </summary>
public class SiteListPublisher : ISiteListPublisher
{
    private readonly ISiteListValidator _validator;
    private readonly IApplicationRepository _repository;
    private readonly ILogger<SiteListPublisher> _logger;

    public SiteListPublisher(
        ISiteListValidator validator,
        IApplicationRepository repository,
        ILogger<SiteListPublisher> logger)
    {
        _validator = validator;
        _repository = repository;
        _logger = logger;
    }

    public async Task<PublishResult> PublishAsync(SiteListDocument document, CancellationToken ct)
    {
        var validation = _validator.Validate(document);
        if (!validation.IsValid)
        {
            var message = "The site list failed validation and was not published:\n  - " +
                          string.Join("\n  - ", validation.Errors);
            await _repository.RecordSiteListVersionAsync(new SiteListVersionRecord
            {
                VersionNumber = document.Version,
                XmlHash = Sha256Of(document.Xml),
                PublishedFilePath = string.Empty,
                PublishedBy = Environment.UserName,
                ValidationSucceeded = false,
                ValidationMessage = string.Join("; ", validation.Errors)
            }, ct);
            return new PublishResult(false, message, document.Version, null, null);
        }

        try
        {
            Directory.CreateDirectory(AppConstants.SiteListDirectory);
            var target = AppConstants.SiteListFilePath;

            // Keep the outgoing file as a versioned backup before it is replaced.
            string? backupPath = null;
            if (File.Exists(target))
            {
                backupPath = Path.Combine(AppConstants.SiteListDirectory, $"sitelist.v{document.Version - 1}.bak.xml");
                File.Copy(target, backupPath, overwrite: true);
            }

            var temp = target + ".tmp";
            await File.WriteAllTextAsync(temp, document.Xml, new UTF8Encoding(false), ct);
            File.Move(temp, target, overwrite: true);

            var hash = Sha256Of(document.Xml);
            await _repository.RecordSiteListVersionAsync(new SiteListVersionRecord
            {
                VersionNumber = document.Version,
                XmlHash = hash,
                PublishedFilePath = target,
                PublishedBy = Environment.UserName,
                ValidationSucceeded = true,
                ValidationMessage = validation.Warnings.Count > 0 ? string.Join("; ", validation.Warnings) : null,
                BackupFilePath = backupPath
            }, ct);

            RestartFlag.Set();
            _logger.LogInformation("Published site list version {Version} ({Count} rules) to {Path}",
                document.Version, document.Entries.Count, target);

            var okMessage = $"Site list version {document.Version} published ({document.Entries.Count} rules). " +
                            "Microsoft Edge must be restarted to pick up the change.";
            if (validation.Warnings.Count > 0)
                okMessage += "\nWarnings:\n  - " + string.Join("\n  - ", validation.Warnings);

            return new PublishResult(true, okMessage, document.Version, target, hash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish site list version {Version}", document.Version);
            return new PublishResult(false, "Failed to write the site list file.", document.Version, null, null, ex.Message);
        }
    }

    public async Task<RestoreResult> RollbackAsync(CancellationToken ct)
    {
        try
        {
            var history = await _repository.GetSiteListHistoryAsync(ct);
            var current = history.FirstOrDefault(v => v.ValidationSucceeded);
            if (current is null)
                return new RestoreResult(false, "There is no published site list version to roll back.");

            if (string.IsNullOrEmpty(current.BackupFilePath) || !File.Exists(current.BackupFilePath))
                return new RestoreResult(false,
                    $"Version {current.VersionNumber} has no backup of the previous file, so it cannot be rolled back.");

            File.Copy(current.BackupFilePath, AppConstants.SiteListFilePath, overwrite: true);
            RestartFlag.Set();

            _logger.LogWarning("Rolled back site list from version {Version} to its previous file", current.VersionNumber);
            return new RestoreResult(true,
                $"Site list rolled back to the file that preceded version {current.VersionNumber}. " +
                "Microsoft Edge must be restarted to pick up the change.",
                EdgeRestartRequired: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Site list rollback failed");
            return new RestoreResult(false, "Site list rollback failed.", false, ex.Message);
        }
    }

    private static string Sha256Of(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
