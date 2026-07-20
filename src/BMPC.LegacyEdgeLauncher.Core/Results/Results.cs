using BMPC.LegacyEdgeLauncher.Core.Enums;

namespace BMPC.LegacyEdgeLauncher.Core.Results;

/// <summary>Base immutable operation result carrying success state and user-facing messaging.</summary>
public record OperationResult(bool Succeeded, string Message, string? TechnicalDetails = null)
{
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public static OperationResult Ok(string message = "OK") => new(true, message);
    public static OperationResult Fail(string message, string? details = null) => new(false, message, details);
}

/// <summary>State of one required Edge policy value.</summary>
public record PolicyValueState(
    string ValueName,
    string? ExpectedValue,
    string? CurrentValue,
    string? ValueKind,
    PolicyValueStatus Status,
    bool ManagedByGroupPolicy);

/// <summary>Aggregate state of all IE-mode related Edge policies.</summary>
public record EdgePolicyState(
    IReadOnlyList<PolicyValueState> Values,
    bool IeModeEnabled,
    string? SiteListUrl,
    bool RestartRequired)
{
    public bool AllCorrect => Values.All(v => v.Status == PolicyValueStatus.Correct);
}

/// <summary>Desired Edge policy configuration to apply.</summary>
public record EdgePolicyConfiguration(
    string SiteListUrl,
    bool AllowManualReload = false,
    PolicyScope Scope = PolicyScope.Machine);

public record PolicyChangeResult(bool Succeeded, string Message, IReadOnlyList<Guid> SnapshotIds, bool RestartRequired, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

public record PublishResult(bool Succeeded, string Message, long Version, string? FilePath, string? Sha256Hash, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

public record LaunchResult(bool Succeeded, string Message, string? BlockedReason = null, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

public record BackupResult(bool Succeeded, string Message, Guid? BackupId = null, string? BackupPath = null, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

/// <summary>Outcome of exporting the application config to a file.</summary>
public record ConfigExportResult(bool Succeeded, string Message, int ApplicationCount = 0, string? FilePath = null, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

/// <summary>Outcome of importing (merging) an application config file.</summary>
public record ConfigImportResult(bool Succeeded, string Message, int Added = 0, int Updated = 0, int Skipped = 0, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

public record RestoreResult(bool Succeeded, string Message, bool EdgeRestartRequired = false, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

public record ElevationResult(bool Succeeded, string Message, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

public record SecurityZoneState(string Host, int? CurrentZone, string CurrentZoneName, bool ManagedByPolicy);

public record SecurityZoneChangeResult(bool Succeeded, string Message, Guid? SnapshotId = null, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

/// <summary>State of one legacy .NET URL action in the Trusted Sites zone.</summary>
public record TrustedZoneDotNetSettingState(
    string ValueName,
    string DisplayName,
    int ExpectedValue,
    int? CurrentValue)
{
    public bool IsConfigured => CurrentValue == ExpectedValue;
}

/// <summary>Aggregate state of the narrowly selected .NET settings required by the legacy site.</summary>
public record TrustedZoneDotNetState(
    IReadOnlyList<TrustedZoneDotNetSettingState> Settings,
    bool ManagedByGroupPolicy)
{
    public bool AllConfigured => Settings.All(s => s.IsConfigured);
}

public record TrustedZoneConfigurationResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<Guid> SnapshotIds,
    string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

public record ContentPolicyState(IReadOnlyList<string> PopupsAllowedForUrls, bool ManagedByGroupPolicy);

public record ContentPolicyChangeResult(bool Succeeded, string Message, Guid? SnapshotId = null, string? TechnicalDetails = null)
    : OperationResult(Succeeded, Message, TechnicalDetails);

/// <summary>Result of validating a generated site-list document.</summary>
public record SiteListValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public static SiteListValidationResult Valid() => new(true, Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>Full diagnostic report for one run.</summary>
public record DiagnosticReport(
    Guid RunId,
    string CorrelationId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    DiagnosticStatus OverallStatus,
    IReadOnlyList<Models.DiagnosticResult> Results);
