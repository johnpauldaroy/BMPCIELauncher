using BMPC.LegacyEdgeLauncher.Core.Enums;

namespace BMPC.LegacyEdgeLauncher.Core.Models;

/// <summary>
/// Portable, human-readable snapshot of the registered applications, written by Export config
/// and consumed by Import config. Deliberately scoped to application data only — Edge registry
/// policies are machine-specific and are applied separately via the Policies &amp; Setup page.
/// The <see cref="SchemaVersion"/> lets future imports detect and adapt to format changes.
/// </summary>
public class ConfigExport
{
    /// <summary>Current on-disk format version. Bump when the shape changes incompatibly.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string ProductName { get; set; } = string.Empty;
    public string ProductVersion { get; set; } = string.Empty;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
    public string ExportedBy { get; set; } = string.Empty;

    public List<ExportedApplication> Applications { get; set; } = new();
}

/// <summary>One application in an exported config. Excludes machine-local audit columns
/// (timestamps, created-by) so the file is portable and stable across machines.</summary>
public class ExportedApplication
{
    public string Name { get; set; } = string.Empty;
    public EnvironmentType Environment { get; set; } = EnvironmentType.Production;
    public string BaseUrl { get; set; } = string.Empty;
    public string? PathRule { get; set; }
    public OpenInMode OpenIn { get; set; } = OpenInMode.IE11;
    public BrowserLaunchMode LaunchBrowser { get; set; } = BrowserLaunchMode.EdgeIeMode;
    public string CompatibilityMode { get; set; } = nameof(CompatMode.Default);
    public string IconKey { get; set; } = Constants.AppIcons.DefaultKey;
    public string? Description { get; set; }
    public string? Owner { get; set; }
    public string? Department { get; set; }
    public string? SupportContact { get; set; }
    public string? Notes { get; set; }
    public bool IsEnabled { get; set; } = true;

    public List<ExportedNeutralSite> NeutralSites { get; set; } = new();
}

/// <summary>One neutral (authentication) host associated with an exported application.</summary>
public class ExportedNeutralSite
{
    public string Url { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsEnabled { get; set; } = true;
}
