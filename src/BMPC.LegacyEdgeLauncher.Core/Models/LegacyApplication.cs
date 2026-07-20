using BMPC.LegacyEdgeLauncher.Core.Enums;

namespace BMPC.LegacyEdgeLauncher.Core.Models;

/// <summary>A registered legacy web system approved to open in Edge IE mode.</summary>
public class LegacyApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public EnvironmentType Environment { get; set; } = EnvironmentType.Production;

    /// <summary>Canonical launch URL (always stored with an explicit http/https scheme).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Lower-cased host (plus port when non-default) used for site-list matching.</summary>
    public string NormalizedHost { get; set; } = string.Empty;

    /// <summary>Optional path prefix used to narrow the site-list rule (e.g. "/barbazampc").</summary>
    public string? PathRule { get; set; }

    public OpenInMode OpenIn { get; set; } = OpenInMode.IE11;
    public BrowserLaunchMode LaunchBrowser { get; set; } = BrowserLaunchMode.EdgeIeMode;
    public string CompatibilityMode { get; set; } = nameof(CompatMode.Default);

    public string? Owner { get; set; }
    public string? Department { get; set; }
    public string? SupportContact { get; set; }
    public string? Notes { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Key identifying which dashboard icon to show for this system (see
    /// <see cref="Constants.AppIcons"/>). Purely presentational.
    /// </summary>
    public string IconKey { get; set; } = Constants.AppIcons.DefaultKey;

    /// <summary>Short one-line subtitle shown on the dashboard tile (e.g. "Core banking portal.").</summary>
    public string? Description { get; set; }

    public DateTimeOffset? LastTestedAt { get; set; }
    public string? LastTestStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string UpdatedBy { get; set; } = string.Empty;

    public List<NeutralSite> NeutralSites { get; set; } = new();

    // Modernization tracking (section 26)
    public bool RequiresActiveX { get; set; }
    public string? ReasonIeModeRequired { get; set; }
    public string? RiskLevel { get; set; }
    public string? ModernizationOwner { get; set; }
    public DateTimeOffset? TargetReplacementDate { get; set; }
    public DateTimeOffset? LastCompatibilityReview { get; set; }

    /// <summary>The site-list rule URL: normalized host plus optional path rule.</summary>
    public string SiteListRuleUrl =>
        string.IsNullOrWhiteSpace(PathRule)
            ? NormalizedHost
            : NormalizedHost + (PathRule.StartsWith('/') ? PathRule : "/" + PathRule);

    public string LaunchBrowserDisplayName => LaunchBrowser switch
    {
        BrowserLaunchMode.EdgeIeMode => "Edge (IE mode)",
        BrowserLaunchMode.MicrosoftEdge => "Microsoft Edge",
        BrowserLaunchMode.InternetExplorerLegacy => "Internet Explorer (legacy)",
        _ => LaunchBrowser.ToString()
    };
}
