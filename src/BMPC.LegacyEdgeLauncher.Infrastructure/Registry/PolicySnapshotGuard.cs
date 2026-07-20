using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Models;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Registry;

/// <summary>Constrains persisted rollback records before they reach a registry API.</summary>
internal static class PolicySnapshotGuard
{
    private static readonly IReadOnlyDictionary<string, string> EdgePolicyValueTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AppConstants.IntegrationLevelValueName] = "DWord",
            [AppConstants.SiteListValueName] = "String",
            [AppConstants.ReloadAllowedValueName] = "DWord"
        };

    public static bool IsValidEdgePolicySnapshot(PolicySnapshot snapshot, out string reason)
    {
        if (!HasExpectedIdentity(snapshot, "Machine", AppConstants.EdgePolicyKeyPath, out reason))
            return false;

        if (!EdgePolicyValueTypes.TryGetValue(snapshot.ValueName, out var expectedType))
        {
            reason = "The snapshot value name is not an approved Edge policy value.";
            return false;
        }

        if (!HasExpectedTypes(snapshot, expectedType, out reason))
            return false;

        if (expectedType == "DWord" &&
            ((snapshot.PreviousValue is not null && !int.TryParse(snapshot.PreviousValue, out _)) ||
             snapshot.NewValue is null || !int.TryParse(snapshot.NewValue, out _)))
        {
            reason = "The snapshot contains an invalid DWORD value.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool IsValidPopupSnapshot(PolicySnapshot snapshot, out string reason)
    {
        if (!HasExpectedIdentity(snapshot, "Machine", AppConstants.PopupsAllowedKeyPath, out reason))
            return false;

        if (!int.TryParse(snapshot.ValueName, out var slot) || slot <= 0)
        {
            reason = "The snapshot does not identify a valid pop-up allowlist slot.";
            return false;
        }

        // This service only creates new free slots, so rollback may only delete that slot.
        if (snapshot.PreviousValue is not null || snapshot.PreviousValueType is not null ||
            !string.Equals(snapshot.NewValueType, "String", StringComparison.Ordinal) ||
            !IsHttpOrigin(snapshot.NewValue))
        {
            reason = "The snapshot is not a pop-up allowlist record created by this service.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool IsValidTrustedSitesSnapshot(PolicySnapshot snapshot, out string reason)
    {
        if (snapshot.RestoredAt is not null)
        {
            reason = "The snapshot has already been restored.";
            return false;
        }

        if (!string.Equals(snapshot.Scope, "User", StringComparison.Ordinal) ||
            !IsChildRegistryPath(AppConstants.ZoneMapDomainsKeyPath, snapshot.RegistryPath))
        {
            reason = "The snapshot is outside the current-user Trusted Sites registry scope.";
            return false;
        }

        if (!AppConstants.AllowedSchemes.Contains(snapshot.ValueName) ||
            !string.Equals(snapshot.NewValueType, "DWord", StringComparison.Ordinal) ||
            snapshot.NewValue != AppConstants.TrustedSitesZone.ToString())
        {
            reason = "The snapshot is not a Trusted Sites mapping created by this service.";
            return false;
        }

        if (snapshot.PreviousValue is null)
        {
            if (snapshot.PreviousValueType is not null)
            {
                reason = "The snapshot has an inconsistent previous value type.";
                return false;
            }
        }
        else if (!string.Equals(snapshot.PreviousValueType, "DWord", StringComparison.Ordinal) ||
                 !int.TryParse(snapshot.PreviousValue, out var zone) || zone is < 0 or > 4)
        {
            reason = "The snapshot contains an invalid previous security-zone value.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool IsValidTrustedZoneDotNetSnapshot(PolicySnapshot snapshot, out string reason)
    {
        // Single source of truth for which Trusted Sites URL actions this tool may write/restore.
        // Kept in sync with TrustedSitesService.DotNetSettings. Deliberately excludes the
        // arbitrary-code-execution actions (unsafe/unsigned ActiveX, launch-without-prompt).
        var expectedValues = TrustedSitesService.CompatibilitySettings
            .ToDictionary(s => s.ValueName, s => s.ExpectedValue, StringComparer.Ordinal);

        if (!HasExpectedIdentity(snapshot, "User", AppConstants.TrustedSitesZoneSettingsPath, out reason))
            return false;

        if (!expectedValues.TryGetValue(snapshot.ValueName, out var expectedValue) ||
            !string.Equals(snapshot.NewValueType, "DWord", StringComparison.Ordinal) ||
            snapshot.NewValue != expectedValue.ToString())
        {
            reason = "The snapshot is not an approved Trusted Sites compatibility setting.";
            return false;
        }

        if (snapshot.PreviousValue is null)
        {
            if (snapshot.PreviousValueType is not null)
            {
                reason = "The snapshot has an inconsistent previous value type.";
                return false;
            }
        }
        else if (!string.Equals(snapshot.PreviousValueType, "DWord", StringComparison.Ordinal) ||
                 !int.TryParse(snapshot.PreviousValue, out _))
        {
            reason = "The snapshot contains an invalid previous DWORD value.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool HasExpectedIdentity(
        PolicySnapshot snapshot, string scope, string registryPath, out string reason)
    {
        if (snapshot.RestoredAt is not null)
        {
            reason = "The snapshot has already been restored.";
            return false;
        }

        if (!string.Equals(snapshot.Scope, scope, StringComparison.Ordinal) ||
            !string.Equals(snapshot.RegistryPath, registryPath, StringComparison.OrdinalIgnoreCase))
        {
            reason = "The snapshot is outside the registry scope owned by this service.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool HasExpectedTypes(PolicySnapshot snapshot, string expectedType, out string reason)
    {
        if (!string.Equals(snapshot.NewValueType, expectedType, StringComparison.Ordinal) ||
            snapshot.NewValue is null ||
            (snapshot.PreviousValue is null
                ? snapshot.PreviousValueType is not null
                : !string.Equals(snapshot.PreviousValueType, expectedType, StringComparison.Ordinal)))
        {
            reason = "The snapshot value types are inconsistent with the approved policy value.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsHttpOrigin(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        AppConstants.AllowedSchemes.Contains(uri.Scheme) &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        uri.UserInfo.Length == 0 &&
        uri.AbsolutePath == "/" &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);

    private static bool IsChildRegistryPath(string parent, string candidate)
    {
        var prefix = parent.TrimEnd('\\') + "\\";
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var relative = candidate[prefix.Length..];
        return relative.Length > 0 &&
               relative.Split('\\').All(part => part.Length > 0 && part is not "." and not "..");
    }
}
