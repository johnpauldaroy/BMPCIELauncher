using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using BMPC.LegacyEdgeLauncher.Core.Enums;

namespace BMPC.LegacyEdgeLauncher.Core.Interfaces;

/// <summary>Reads, applies, and restores the documented Edge IE-mode registry policies.</summary>
public interface IEdgePolicyService
{
    Task<EdgePolicyState> ReadStateAsync(CancellationToken cancellationToken);
    Task<PolicyChangeResult> ApplyAsync(EdgePolicyConfiguration configuration, CancellationToken cancellationToken);
    Task<PolicyChangeResult> RestoreAsync(Guid snapshotId, CancellationToken cancellationToken);
}

/// <summary>In-memory representation of a generated Enterprise Mode Site List.</summary>
public record SiteListDocument(long Version, string Xml, IReadOnlyList<SiteListEntry> Entries);

/// <summary>One &lt;site&gt; rule in the generated document.</summary>
public record SiteListEntry(string Url, string OpenIn, string CompatMode);

public interface ISiteListGenerator
{
    SiteListDocument Generate(
        IReadOnlyCollection<LegacyApplication> applications,
        IReadOnlyCollection<NeutralSite> neutralSites,
        long version);
}

public interface ISiteListValidator
{
    SiteListValidationResult Validate(SiteListDocument document);
}

public interface ISiteListPublisher
{
    Task<PublishResult> PublishAsync(SiteListDocument document, CancellationToken cancellationToken);
    Task<RestoreResult> RollbackAsync(CancellationToken cancellationToken);
}

public interface IEdgeLauncher
{
    string? FindEdgeExecutable();
    string? GetEdgeVersion();
    bool IsEdgeRunning();
    Task<LaunchResult> OpenAsync(Uri url, BrowserLaunchMode browser, CancellationToken cancellationToken, string? compatibilityMode = null);
    Task<OperationResult> RestartEdgeAsync(bool force, CancellationToken cancellationToken);
}

public interface IDiagnosticService
{
    Task<DiagnosticReport> RunAsync(Guid? applicationId, CancellationToken cancellationToken);
}

public interface IBackupService
{
    Task<BackupResult> CreateAsync(string reason, CancellationToken cancellationToken);
    Task<RestoreResult> RestoreAsync(Guid backupId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Metadata describing one stored backup set.</summary>
public record BackupInfo(Guid Id, DateTimeOffset CreatedAt, string Reason, string CreatedBy, string Path, string Hash);

/// <summary>
/// Exports the registered applications to a portable JSON file and imports them back,
/// merging into the current set (add new, update existing). Application data only —
/// Edge registry policies are handled separately.
/// </summary>
public interface IConfigPortabilityService
{
    /// <summary>Serializes all applications to a JSON config file at <paramref name="filePath"/>.</summary>
    Task<ConfigExportResult> ExportAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Reads a config file and merges its applications into the store (add/update, never delete).</summary>
    Task<ConfigImportResult> ImportAsync(string filePath, CancellationToken cancellationToken);
}

public interface IAuditService
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> QueryAsync(DateTimeOffset? from, DateTimeOffset? to, string? action, CancellationToken cancellationToken);
}

public interface IPrivilegeService
{
    bool IsAdministrator();
    Task<ElevationResult> RelaunchElevatedAsync(
        ElevatedCommand command,
        IReadOnlyList<string>? arguments,
        CancellationToken cancellationToken);
}

/// <summary>Manages Internet Options security-zone (Trusted Sites) assignment (section 37).</summary>
public interface IInternetSecurityZoneService
{
    Task<SecurityZoneState> ReadAsync(Uri uri, CancellationToken cancellationToken);
    Task<SecurityZoneChangeResult> AddToTrustedSitesAsync(Uri uri, CancellationToken cancellationToken);
    Task<TrustedZoneDotNetState> ReadTrustedZoneDotNetStateAsync(CancellationToken cancellationToken);
    Task<TrustedZoneConfigurationResult> ConfigureTrustedZoneDotNetAsync(CancellationToken cancellationToken);
    Task<SecurityZoneChangeResult> RestoreAsync(Guid snapshotId, CancellationToken cancellationToken);
}

/// <summary>Manages the Edge PopupsAllowedForUrls allowlist (section 39).</summary>
public interface IEdgeContentPolicyService
{
    Task<ContentPolicyState> ReadAsync(Uri uri, CancellationToken cancellationToken);
    Task<ContentPolicyChangeResult> AllowPopupsForUrlAsync(Uri uri, CancellationToken cancellationToken);
    Task<ContentPolicyChangeResult> RestoreAsync(Guid snapshotId, CancellationToken cancellationToken);

    /// <summary>True when every extension in <see cref="Constants.AppConstants.ReportAutoOpenExtensions"/>
    /// is present in Edge's AutoOpenFileTypes policy.</summary>
    Task<bool> IsReportAutoOpenConfiguredAsync(CancellationToken cancellationToken);

    /// <summary>Adds the report extensions to Edge's AutoOpenFileTypes policy so reports open
    /// directly in their associated viewer instead of prompting to download.</summary>
    Task<ContentPolicyChangeResult> EnableReportAutoOpenAsync(CancellationToken cancellationToken);
}

/// <summary>Repository over the SQLite store for applications and related records.</summary>
public interface IApplicationRepository
{
    Task<IReadOnlyList<LegacyApplication>> GetAllAsync(CancellationToken cancellationToken);
    Task<LegacyApplication?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<OperationResult> UpsertAsync(LegacyApplication application, CancellationToken cancellationToken);
    Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<long> GetNextSiteListVersionAsync(CancellationToken cancellationToken);
    Task RecordSiteListVersionAsync(SiteListVersionRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<SiteListVersionRecord>> GetSiteListHistoryAsync(CancellationToken cancellationToken);
    Task<Guid> SavePolicySnapshotAsync(PolicySnapshot snapshot, CancellationToken cancellationToken);
    Task<PolicySnapshot?> GetPolicySnapshotAsync(Guid id, CancellationToken cancellationToken);
    Task MarkSnapshotRestoredAsync(Guid id, CancellationToken cancellationToken);
}
