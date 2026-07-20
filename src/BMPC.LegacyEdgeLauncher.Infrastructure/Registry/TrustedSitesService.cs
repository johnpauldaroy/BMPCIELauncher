using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Registry;

/// <summary>
/// Manages Internet Options security-zone assignment (Trusted Sites) for a single exact host
/// (section 37). Writes HTTPS-specific per-user zone mappings, never wildcards a parent domain,
/// and backs up the previous mapping before every change.
/// </summary>
public class TrustedSitesService : IInternetSecurityZoneService
{
    private static readonly string[] ZoneNames =
        { "Computer", "Local Intranet", "Trusted Sites", "Internet", "Restricted Sites" };

    /// <summary>
    /// Trusted Sites zone URL actions this tool is permitted to enable (Microsoft security-zone
    /// action codes). Written to the Trusted Sites zone ONLY — no Internet-zone setting is changed.
    /// Value 0 = Enable, 0x00010000 = High Safety, 1 = Prompt. This is the compatibility set that
    /// legacy web apps such as NATCCO CBS require. It deliberately EXCLUDES the arbitrary-code-
    /// execution actions (1004/1201 unsafe or unsigned ActiveX, 1806 launch apps/unsafe files,
    /// 1809 IFRAME launch, 2001/2004 unsafe .NET manifest components), which are never enabled
    /// automatically (spec sections 38 and 32); manifest components stay at High Safety.
    /// Public so <see cref="PolicySnapshotGuard"/> validates rollback against the exact same set,
    /// keeping the writer and the restore guard from drifting apart.
    /// </summary>
    public static readonly IReadOnlyList<(string ValueName, string DisplayName, int ExpectedValue)> CompatibilitySettings =
        new[]
        {
            // Legacy .NET rendering actions (already required for CASA)
            ("2400", "XAML browser applications", 0),
            ("2401", "XPS documents", 0),
            ("2402", "Loose XAML", 0),
            ("2007", "Permissions for components with manifests (High Safety)", 0x00010000),

            // Compatibility set: what legacy web apps need to render menus, scripts and downloads
            ("1400", "Active scripting", 0),
            ("1402", "Scripting of Java applets", 0),
            ("2000", "Binary and script behaviors", 0),
            ("1803", "File download", 0),
            ("1604", "Font download", 0),
            ("1608", "Allow META REFRESH", 0),
            ("1609", "Display mixed content", 1),          // Prompt, not silent-allow
            ("1802", "Drag and drop or copy and paste files", 0),
            ("1206", "Allow scripting of Internet Explorer WebBrowser control", 0)
        };

    private readonly IApplicationRepository _repository;
    private readonly ILogger<TrustedSitesService> _logger;

    public TrustedSitesService(IApplicationRepository repository, ILogger<TrustedSitesService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task<SecurityZoneState> ReadAsync(Uri uri, CancellationToken ct)
    {
        var (domainKeyPath, subKeyPath) = GetZoneMapKeyPaths(uri.Host);

        // Policy-managed mappings (Site to Zone Assignment List) take precedence — detect, never overwrite.
        var managedByPolicy = IsPolicyManaged(uri);

        int? zone = ReadZone(Microsoft.Win32.Registry.CurrentUser, subKeyPath, uri.Scheme)
                    ?? ReadZone(Microsoft.Win32.Registry.CurrentUser, domainKeyPath, uri.Scheme)
                    ?? ReadZone(Microsoft.Win32.Registry.LocalMachine, subKeyPath, uri.Scheme)
                    ?? ReadZone(Microsoft.Win32.Registry.LocalMachine, domainKeyPath, uri.Scheme);

        var zoneName = zone is >= 0 and < 5 ? ZoneNames[zone.Value] : "Not assigned (default Internet zone)";
        return Task.FromResult(new SecurityZoneState(uri.Host, zone, zoneName, managedByPolicy));
    }

    public async Task<SecurityZoneChangeResult> AddToTrustedSitesAsync(Uri uri, CancellationToken ct)
    {
        var state = await ReadAsync(uri, ct);
        if (state.ManagedByPolicy)
        {
            return new SecurityZoneChangeResult(false,
                "The security zone for this host is managed by organizational policy (Site to Zone Assignment List). " +
                "It cannot be changed locally. Contact your domain administrator.");
        }

        try
        {
            var (_, subKeyPath) = GetZoneMapKeyPaths(uri.Host);

            var snapshotId = await _repository.SavePolicySnapshotAsync(new PolicySnapshot
            {
                Scope = "User",
                RegistryPath = subKeyPath,
                ValueName = uri.Scheme,
                PreviousValue = state.CurrentZone?.ToString(),
                PreviousValueType = state.CurrentZone is null ? null : "DWord",
                NewValue = AppConstants.TrustedSitesZone.ToString(),
                NewValueType = "DWord",
                ChangedBy = Environment.UserName
            }, ct);

            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKeyPath)
                ?? throw new InvalidOperationException("Unable to create the zone map key.");
            key.SetValue(uri.Scheme, AppConstants.TrustedSitesZone, RegistryValueKind.DWord);

            _logger.LogInformation("Added {Host} ({Scheme}) to Trusted Sites for the current user", uri.Host, uri.Scheme);
            return new SecurityZoneChangeResult(true,
                $"'{uri.Host}' ({uri.Scheme.ToUpperInvariant()}) is now assigned to Trusted Sites for the current user.",
                snapshotId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add {Host} to Trusted Sites", uri.Host);
            return new SecurityZoneChangeResult(false, "Failed to update the Trusted Sites zone mapping.", null, ex.Message);
        }
    }

    public Task<TrustedZoneDotNetState> ReadTrustedZoneDotNetStateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppConstants.TrustedSitesZoneSettingsPath);
        var settings = CompatibilitySettings
            .Select(setting => new TrustedZoneDotNetSettingState(
                setting.ValueName,
                setting.DisplayName,
                setting.ExpectedValue,
                key?.GetValue(setting.ValueName) as int?))
            .ToList();

        return Task.FromResult(new TrustedZoneDotNetState(settings, AreTrustedZoneSettingsManaged()));
    }

    public async Task<TrustedZoneConfigurationResult> ConfigureTrustedZoneDotNetAsync(CancellationToken ct)
    {
        if (AreTrustedZoneSettingsManaged())
        {
            return new TrustedZoneConfigurationResult(false,
                "The Trusted Sites security settings are managed by organizational policy and cannot be changed locally.",
                Array.Empty<Guid>());
        }

        var snapshotIds = new List<Guid>();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AppConstants.TrustedSitesZoneSettingsPath)
                ?? throw new InvalidOperationException("Unable to open the Trusted Sites security settings.");

            foreach (var setting in CompatibilitySettings)
            {
                ct.ThrowIfCancellationRequested();
                var previous = key.GetValue(setting.ValueName) as int?;
                if (previous == setting.ExpectedValue)
                    continue;

                var snapshotId = await _repository.SavePolicySnapshotAsync(new PolicySnapshot
                {
                    Scope = "User",
                    RegistryPath = AppConstants.TrustedSitesZoneSettingsPath,
                    ValueName = setting.ValueName,
                    PreviousValue = previous?.ToString(),
                    PreviousValueType = previous is null ? null : "DWord",
                    NewValue = setting.ExpectedValue.ToString(),
                    NewValueType = "DWord",
                    ChangedBy = Environment.UserName
                }, ct);

                key.SetValue(setting.ValueName, setting.ExpectedValue, RegistryValueKind.DWord);
                snapshotIds.Add(snapshotId);
            }

            _logger.LogWarning(
                "Configured {Count} legacy .NET security actions for the current user's Trusted Sites zone",
                snapshotIds.Count);

            if (snapshotIds.Count > 0)
                RestartFlag.Set();

            var message = snapshotIds.Count == 0
                ? "The required compatibility and legacy .NET settings are already configured for Trusted Sites."
                : $"Enabled {snapshotIds.Count} compatibility setting(s) for Trusted Sites (active scripting, downloads, " +
                  "fonts, behaviors, XAML/XPS), while keeping unsafe ActiveX and launch-without-prompt disabled. " +
                  "This applies to every site in the Trusted Sites zone. Restart Microsoft Edge to apply the change.";
            return new TrustedZoneConfigurationResult(true, message, snapshotIds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure legacy .NET actions for Trusted Sites");
            return new TrustedZoneConfigurationResult(false,
                "Failed to configure the required legacy .NET settings for Trusted Sites.", snapshotIds, ex.Message);
        }
    }

    public async Task<SecurityZoneChangeResult> RestoreAsync(Guid snapshotId, CancellationToken ct)
    {
        var snapshot = await _repository.GetPolicySnapshotAsync(snapshotId, ct);
        if (snapshot is null)
            return new SecurityZoneChangeResult(false, "Zone snapshot not found.");

        // Persisted rollback data is untrusted at this privilege boundary.
        if (!PolicySnapshotGuard.IsValidTrustedSitesSnapshot(snapshot, out var mappingError) &&
            !PolicySnapshotGuard.IsValidTrustedZoneDotNetSnapshot(snapshot, out var settingError))
        {
            _logger.LogWarning("Rejected unsafe Trusted Sites snapshot {Id}: {MappingError}; {SettingError}",
                snapshotId, mappingError, settingError);
            return new SecurityZoneChangeResult(false,
                "The zone snapshot is invalid or is not eligible for restore.");
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(snapshot.RegistryPath)
                ?? throw new InvalidOperationException("Unable to open the zone map key.");

            if (snapshot.PreviousValue is null)
                key.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
            else
                key.SetValue(snapshot.ValueName, int.Parse(snapshot.PreviousValue), RegistryValueKind.DWord);

            await _repository.MarkSnapshotRestoredAsync(snapshotId, ct);
            return new SecurityZoneChangeResult(true, "Previous Trusted Sites setting restored.", snapshotId);
        }
        catch (Exception ex)
        {
            return new SecurityZoneChangeResult(false, "Failed to restore the zone mapping.", null, ex.Message);
        }
    }

    /// <summary>
    /// ZoneMap layout: Domains\{registrable-domain}\{subdomain} with a value named after the
    /// scheme. cbs.natcco.coop becomes Domains\natcco.coop\cbs with "https"=2.
    /// Only the exact host is mapped — never a *.domain wildcard (section 37.1).
    /// </summary>
    private static (string DomainKeyPath, string SubKeyPath) GetZoneMapKeyPaths(string host)
    {
        var labels = host.Split('.');
        if (labels.Length <= 2)
        {
            var path = $@"{AppConstants.ZoneMapDomainsKeyPath}\{host}";
            return (path, path);
        }

        var domain = string.Join('.', labels[^2..]);
        var sub = string.Join('.', labels[..^2]);
        return ($@"{AppConstants.ZoneMapDomainsKeyPath}\{domain}",
                $@"{AppConstants.ZoneMapDomainsKeyPath}\{domain}\{sub}");
    }

    private static int? ReadZone(RegistryKey root, string keyPath, string scheme)
    {
        using var key = root.OpenSubKey(keyPath);
        return key?.GetValue(scheme) as int?;
    }

    private static bool IsPolicyManaged(Uri uri)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(AppConstants.ZoneMapEscDomainsPolicyPath);
        if (key is null) return false;
        return key.GetValueNames().Any(n => n.Contains(uri.Host, StringComparison.OrdinalIgnoreCase));
    }

    private static bool AreTrustedZoneSettingsManaged()
    {
        using (var policy = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(AppConstants.InternetSettingsPolicyPath))
        {
            if (policy?.GetValue("Security_HKLM_only") is int machineOnly && machineOnly == 1)
                return true;
        }

        return HasManagedDotNetSetting(Microsoft.Win32.Registry.CurrentUser) ||
               HasManagedDotNetSetting(Microsoft.Win32.Registry.LocalMachine);
    }

    private static bool HasManagedDotNetSetting(RegistryKey root)
    {
        using var key = root.OpenSubKey(AppConstants.TrustedSitesZonePolicyPath);
        return key is not null && CompatibilitySettings.Any(setting => key.GetValue(setting.ValueName) is not null);
    }
}
