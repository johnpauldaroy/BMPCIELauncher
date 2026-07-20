namespace BMPC.LegacyEdgeLauncher.Core.Constants;

/// <summary>Central location for registry paths, policy names, file locations, and defaults.</summary>
public static class AppConstants
{
    public const string ApplicationName = "BMPC IE Launcher";
    public const string Organization = "Barbaza Multi-Purpose Cooperative";
    public const string ApplicationVersion = "1.0.0";

    // Registry
    public const string EdgePolicyKeyPath = @"SOFTWARE\Policies\Microsoft\Edge";
    public const string PopupsAllowedKeyPath = @"SOFTWARE\Policies\Microsoft\Edge\PopupsAllowedForUrls";
    public const string AutoOpenFileTypesKeyPath = @"SOFTWARE\Policies\Microsoft\Edge\AutoOpenFileTypes";

    /// <summary>File extensions (no dot) Edge should auto-open in their associated app instead of prompting.
    /// The .dprt Crystal Reports export is the report format used by EkoopBanker+.</summary>
    public static readonly string[] ReportAutoOpenExtensions = { "dprt" };
    public const string ZoneMapDomainsKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap\Domains";
    public const string ZoneMapEscDomainsPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMapKey";
    public const string TrustedSitesZoneSettingsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Zones\2";
    public const string InternetSettingsPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings";
    public const string TrustedSitesZonePolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Zones\2";

    // Policy value names
    public const string IntegrationLevelValueName = "InternetExplorerIntegrationLevel";
    public const string SiteListValueName = "InternetExplorerIntegrationSiteList";
    public const string ReloadAllowedValueName = "InternetExplorerIntegrationReloadInIEModeAllowed";

    public const int IntegrationLevelIeMode = 1;
    public const int TrustedSitesZone = 2;

    // Site list service defaults
    public const int DefaultServicePort = 8765;

    /// <summary>Loopback HTTP endpoint used when the local site-list Windows service hosts the file.</summary>
    public const string LoopbackSiteListUrl = "http://127.0.0.1:8765/sitelist.xml";

    /// <summary>
    /// Default site-list location for standalone installs: a local <c>file://</c> URL pointing at the
    /// published sitelist.xml. Edge reads it directly, so no background service is required.
    /// </summary>
    public static string DefaultSiteListUrl =>
        new Uri(SiteListFilePath).AbsoluteUri;
    public const string ServiceName = "BMPCLegacyEdgeSiteListService";
    public const string ServiceDisplayName = "BMPC Legacy Edge Site List Service";

    // Data directories
    public const string DataRoot = @"C:\ProgramData\BMPC\LegacyEdgeLauncher";
    public static string DatabaseDirectory => Path.Combine(DataRoot, "Database");
    public static string SiteListDirectory => Path.Combine(DataRoot, "SiteList");
    public static string BackupDirectory => Path.Combine(DataRoot, "Backups");
    public static string LogDirectory => Path.Combine(DataRoot, "Logs");
    public static string ExportDirectory => Path.Combine(DataRoot, "Exports");
    public static string DatabasePath => Path.Combine(DatabaseDirectory, "launcher.db");
    public static string SiteListFilePath => Path.Combine(SiteListDirectory, "sitelist.xml");

    // Allowed launch schemes (section 14.1)
    public static readonly IReadOnlySet<string> AllowedSchemes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    // NATCCO seed profile (section 34) — seeded record, never a hardcoded launch constant.
    public const string NatccoApplicationName = "NATCCO EKoopBanker+ CASA";
    public const string NatccoProductionUrl = "https://cbs.natcco.coop/barbazampc/logon.asp";
    public const string NatccoHost = "cbs.natcco.coop";
    public const string NatccoPathRule = "/barbazampc";
    public const string NatccoSupportContact = "itechline@natcco.coop";
}
