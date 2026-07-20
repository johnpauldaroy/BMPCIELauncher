using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using BMPC.LegacyEdgeLauncher.Core.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Registry;

/// <summary>
/// Reads and writes the documented Edge IE-mode policies under
/// HKLM\SOFTWARE\Policies\Microsoft\Edge. Backs up every value before changing it
/// and restores only values previously changed by this tool (section 5.4).
/// </summary>
public class EdgePolicyService : IEdgePolicyService
{
    private readonly IApplicationRepository _repository;
    private readonly ILogger<EdgePolicyService> _logger;

    public EdgePolicyService(IApplicationRepository repository, ILogger<EdgePolicyService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task<EdgePolicyState> ReadStateAsync(CancellationToken ct)
    {
        var values = new List<PolicyValueState>();
        var managedExternally = IsLikelyDomainManaged();

        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(AppConstants.EdgePolicyKeyPath);

        var (level, levelKind) = ReadValue(key, AppConstants.IntegrationLevelValueName);
        values.Add(Classify(
            AppConstants.IntegrationLevelValueName,
            AppConstants.IntegrationLevelIeMode.ToString(),
            level, levelKind, "DWord", managedExternally));

        // Any configured URL is acceptable (only "missing" is a failure), but show the default
        // endpoint in the Expected column so administrators know what a standalone install uses.
        var (siteList, siteListKind) = ReadValue(key, AppConstants.SiteListValueName);
        var siteListStatus = siteList is null
            ? PolicyValueStatus.Missing
            : siteListKind != "String" ? PolicyValueStatus.WrongType : PolicyValueStatus.Correct;
        values.Add(new PolicyValueState(
            AppConstants.SiteListValueName, AppConstants.DefaultSiteListUrl,
            siteList, siteListKind, siteListStatus, managedExternally));

        var (reload, reloadKind) = ReadValue(key, AppConstants.ReloadAllowedValueName);
        values.Add(new PolicyValueState(
            AppConstants.ReloadAllowedValueName, null, reload, reloadKind,
            reload is null ? PolicyValueStatus.Missing : PolicyValueStatus.Correct,
            managedExternally));

        var ieModeEnabled = level == AppConstants.IntegrationLevelIeMode.ToString();
        var state = new EdgePolicyState(values, ieModeEnabled, siteList, RestartFlag.IsSet());
        return Task.FromResult(state);
    }

    public async Task<PolicyChangeResult> ApplyAsync(EdgePolicyConfiguration configuration, CancellationToken ct)
    {
        // Revalidate at the privileged sink; callers are not a security boundary.
        if (!UrlNormalizer.TryValidateSiteListUrl(configuration.SiteListUrl, out var siteListUri, out var error))
        {
            return new PolicyChangeResult(false, error, Array.Empty<Guid>(), false);
        }

        var snapshotIds = new List<Guid>();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(AppConstants.EdgePolicyKeyPath)
                ?? throw new InvalidOperationException("Unable to open or create the Edge policy key.");

            snapshotIds.Add(await BackupAndSetDwordAsync(key,
                AppConstants.IntegrationLevelValueName, AppConstants.IntegrationLevelIeMode, ct));
            snapshotIds.Add(await BackupAndSetStringAsync(key,
                AppConstants.SiteListValueName, siteListUri!.AbsoluteUri, ct));

            if (configuration.AllowManualReload)
            {
                snapshotIds.Add(await BackupAndSetDwordAsync(key,
                    AppConstants.ReloadAllowedValueName, 1, ct));
            }

            RestartFlag.Set();
            _logger.LogInformation("Edge IE mode policies applied. Site list: {Url}", siteListUri);
            return new PolicyChangeResult(true,
                "Edge IE mode policies applied. Microsoft Edge must be restarted before changes take effect.",
                snapshotIds, RestartRequired: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new PolicyChangeResult(false,
                "Administrator permission is required to change machine policies. Restart the application as administrator.",
                snapshotIds, false, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply Edge policies");
            return new PolicyChangeResult(false, "Failed to apply Edge policies.", snapshotIds, false, ex.Message);
        }
    }

    public async Task<PolicyChangeResult> RestoreAsync(Guid snapshotId, CancellationToken ct)
    {
        var snapshot = await _repository.GetPolicySnapshotAsync(snapshotId, ct);
        if (snapshot is null)
            return new PolicyChangeResult(false, "Policy snapshot not found.", Array.Empty<Guid>(), false);

        // Persisted rollback data is untrusted at this privilege boundary.
        if (!PolicySnapshotGuard.IsValidEdgePolicySnapshot(snapshot, out var validationError))
        {
            _logger.LogWarning("Rejected unsafe Edge policy snapshot {Id}: {Reason}", snapshotId, validationError);
            return new PolicyChangeResult(false, "The policy snapshot is invalid or is not eligible for restore.",
                Array.Empty<Guid>(), false);
        }

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(snapshot.RegistryPath)
                ?? throw new InvalidOperationException("Unable to open the policy key.");

            if (snapshot.PreviousValue is null)
            {
                key.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
            }
            else if (snapshot.PreviousValueType == "DWord")
            {
                key.SetValue(snapshot.ValueName, int.Parse(snapshot.PreviousValue), RegistryValueKind.DWord);
            }
            else
            {
                key.SetValue(snapshot.ValueName, snapshot.PreviousValue, RegistryValueKind.String);
            }

            await _repository.MarkSnapshotRestoredAsync(snapshotId, ct);
            RestartFlag.Set();
            return new PolicyChangeResult(true,
                $"Policy value '{snapshot.ValueName}' restored to its previous state.",
                new[] { snapshotId }, RestartRequired: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new PolicyChangeResult(false,
                "Administrator permission is required to restore machine policies.",
                Array.Empty<Guid>(), false, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore snapshot {Id}", snapshotId);
            return new PolicyChangeResult(false, "Failed to restore the policy value.", Array.Empty<Guid>(), false, ex.Message);
        }
    }

    private async Task<Guid> BackupAndSetDwordAsync(RegistryKey key, string name, int value, CancellationToken ct)
    {
        var (previous, previousKind) = ReadValue(key, name);
        var id = await _repository.SavePolicySnapshotAsync(new PolicySnapshot
        {
            Scope = "Machine",
            RegistryPath = AppConstants.EdgePolicyKeyPath,
            ValueName = name,
            PreviousValue = previous,
            PreviousValueType = previousKind,
            NewValue = value.ToString(),
            NewValueType = "DWord",
            ChangedBy = Environment.UserName
        }, ct);
        key.SetValue(name, value, RegistryValueKind.DWord);
        return id;
    }

    private async Task<Guid> BackupAndSetStringAsync(RegistryKey key, string name, string value, CancellationToken ct)
    {
        var (previous, previousKind) = ReadValue(key, name);
        var id = await _repository.SavePolicySnapshotAsync(new PolicySnapshot
        {
            Scope = "Machine",
            RegistryPath = AppConstants.EdgePolicyKeyPath,
            ValueName = name,
            PreviousValue = previous,
            PreviousValueType = previousKind,
            NewValue = value,
            NewValueType = "String",
            ChangedBy = Environment.UserName
        }, ct);
        key.SetValue(name, value, RegistryValueKind.String);
        return id;
    }

    private static (string? Value, string? Kind) ReadValue(RegistryKey? key, string name)
    {
        if (key is null) return (null, null);
        var value = key.GetValue(name);
        if (value is null) return (null, null);
        return (value.ToString(), key.GetValueKind(name).ToString());
    }

    private static PolicyValueState Classify(
        string name, string? expected, string? current, string? kind, string expectedKind, bool managed)
    {
        PolicyValueStatus status;
        if (current is null)
            status = PolicyValueStatus.Missing;
        else if (kind != expectedKind)
            status = PolicyValueStatus.WrongType;
        else if (expected is not null && current != expected)
            status = PolicyValueStatus.Incorrect;
        else
            status = PolicyValueStatus.Correct;

        return new PolicyValueState(name, expected, current, kind, status, managed);
    }

    /// <summary>
    /// Heuristic only: on a domain-joined machine, HKLM policy values may be enforced by
    /// Group Policy and re-applied on refresh. Used to warn, never to block reads.
    /// </summary>
    private static bool IsLikelyDomainManaged()
    {
        try
        {
            return !string.Equals(Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
