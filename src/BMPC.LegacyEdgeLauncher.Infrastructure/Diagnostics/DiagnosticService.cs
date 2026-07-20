using System.Xml.Linq;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using BMPC.LegacyEdgeLauncher.Core.Validation;
using BMPC.LegacyEdgeLauncher.Infrastructure.Database;
using BMPC.LegacyEdgeLauncher.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Infrastructure.Diagnostics;

/// <summary>
/// Runs the diagnostic checklist (section 22): Edge install, policy state, site-list file
/// and service, and — when an application is given — its rule, trusted-site, and pop-up
/// configuration. Every run is persisted with a correlation id for support escalation.
/// </summary>
public class DiagnosticService : IDiagnosticService
{
    private readonly IEdgeLauncher _edgeLauncher;
    private readonly IEdgePolicyService _policyService;
    private readonly IInternetSecurityZoneService _zoneService;
    private readonly IEdgeContentPolicyService _contentPolicyService;
    private readonly IApplicationRepository _repository;
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiagnosticService> _logger;

    public DiagnosticService(
        IEdgeLauncher edgeLauncher,
        IEdgePolicyService policyService,
        IInternetSecurityZoneService zoneService,
        IEdgeContentPolicyService contentPolicyService,
        IApplicationRepository repository,
        IDbContextFactory<LauncherDbContext> contextFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<DiagnosticService> logger)
    {
        _edgeLauncher = edgeLauncher;
        _policyService = policyService;
        _zoneService = zoneService;
        _contentPolicyService = contentPolicyService;
        _repository = repository;
        _contextFactory = contextFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<DiagnosticReport> RunAsync(Guid? applicationId, CancellationToken ct)
    {
        var run = new DiagnosticRun
        {
            ApplicationId = applicationId,
            ComputerName = Environment.MachineName,
            Username = Environment.UserName
        };
        var results = new List<DiagnosticResult>();

        await CheckSafe(results, CheckEdgeInstalled, ct);
        var policyState = await CheckSafeWithState(results, CheckPoliciesAsync, ct);
        await CheckSafe(results, CheckSiteListFile, ct);
        await CheckSafe(results, c => CheckSiteListServiceAsync(policyState, c), ct);
        await CheckSafe(results, CheckDatabaseAsync, ct);
        await CheckSafe(results, CheckRestartPending, ct);

        if (applicationId is not null)
        {
            var app = await _repository.GetAsync(applicationId.Value, ct);
            if (app is null)
            {
                results.Add(Result("APP_EXISTS", "Application registered", DiagnosticStatus.Failed,
                    "The selected application no longer exists in the database.",
                    action: "Re-register the application, then run diagnostics again."));
            }
            else
            {
                await CheckSafe(results, c => CheckApplicationRuleAsync(app, c), ct);
                await CheckSafe(results, c => CheckTrustedSiteAsync(app, c), ct);
                await CheckSafe(results, c => CheckTrustedZoneDotNetAsync(app, c), ct);
                await CheckSafe(results, c => CheckPopupsAsync(app, c), ct);
            }
        }

        var overall = results.Any(r => r.Status == DiagnosticStatus.Failed) ? DiagnosticStatus.Failed
            : results.Any(r => r.Status == DiagnosticStatus.Warning) ? DiagnosticStatus.Warning
            : DiagnosticStatus.Passed;

        run.CompletedAt = DateTimeOffset.Now;
        run.OverallStatus = overall.ToString();
        foreach (var r in results)
        {
            r.DiagnosticRunId = run.Id;
            run.Results.Add(r);
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(ct);
            db.DiagnosticRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist diagnostic run {Id}", run.Id);
        }

        return new DiagnosticReport(run.Id, run.CorrelationId, run.StartedAt, run.CompletedAt.Value, overall, results);
    }

    private Task<DiagnosticResult> CheckEdgeInstalled(CancellationToken ct)
    {
        var exe = _edgeLauncher.FindEdgeExecutable();
        if (exe is null)
            return Task.FromResult(Result("EDGE_FOUND", "Microsoft Edge installed", DiagnosticStatus.Failed,
                "Microsoft Edge was not found on this computer.",
                action: "Install Microsoft Edge (stable channel), then run diagnostics again."));

        var version = _edgeLauncher.GetEdgeVersion();
        return Task.FromResult(Result("EDGE_FOUND", "Microsoft Edge installed", DiagnosticStatus.Passed,
            $"Microsoft Edge {version ?? "(unknown version)"} found.", exe));
    }

    private async Task<(DiagnosticResult, EdgePolicyState?)> CheckPoliciesAsync(CancellationToken ct)
    {
        var state = await _policyService.ReadStateAsync(ct);
        if (state.IeModeEnabled && state.SiteListUrl is not null)
        {
            return (Result("POLICY_STATE", "Edge IE mode policies", DiagnosticStatus.Passed,
                $"IE mode is enabled and the site list URL is configured ({state.SiteListUrl}).",
                string.Join("; ", state.Values.Select(v => $"{v.ValueName}={v.CurrentValue ?? "(missing)"}"))), state);
        }

        var missing = state.Values.Where(v => v.Status != PolicyValueStatus.Correct).Select(v => v.ValueName);
        var managedNote = state.Values.Any(v => v.ManagedByGroupPolicy)
            ? " This computer appears to be domain-managed; Group Policy may override local changes."
            : string.Empty;
        return (Result("POLICY_STATE", "Edge IE mode policies", DiagnosticStatus.Failed,
            "IE mode is not fully configured." + managedNote,
            "Values needing attention: " + string.Join(", ", missing),
            "Run Setup > Apply IE Mode Policies as administrator."), state);
    }

    private Task<DiagnosticResult> CheckSiteListFile(CancellationToken ct)
    {
        if (!File.Exists(AppConstants.SiteListFilePath))
            return Task.FromResult(Result("SITELIST_FILE", "Site list file", DiagnosticStatus.Failed,
                "No site list has been published yet.",
                AppConstants.SiteListFilePath,
                "Publish the site list from Setup (or 'bmpc-launcher publish')."));

        try
        {
            var xml = XDocument.Load(AppConstants.SiteListFilePath);
            var version = xml.Root?.Attribute("version")?.Value ?? "?";
            var ruleCount = xml.Root?.Elements("site").Count() ?? 0;
            return Task.FromResult(Result("SITELIST_FILE", "Site list file", DiagnosticStatus.Passed,
                $"Site list version {version} with {ruleCount} rules is on disk.", AppConstants.SiteListFilePath));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result("SITELIST_FILE", "Site list file", DiagnosticStatus.Failed,
                "The published site list file is corrupt (not valid XML).", ex.Message,
                "Republish the site list, or restore a backup."));
        }
    }

    private async Task<DiagnosticResult> CheckSiteListServiceAsync(EdgePolicyState? policyState, CancellationToken ct)
    {
        var url = policyState?.SiteListUrl ?? AppConstants.DefaultSiteListUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "file"))
            return Result("SITELIST_URL", "Site list URL reachable", DiagnosticStatus.Failed,
                $"The configured site list URL is not a valid http(s) or file URL: '{url}'.",
                action: "Re-apply the IE mode policies from Setup.");

        // file:// site list — Edge reads it directly; verify the file exists and is readable.
        if (uri.Scheme == "file")
        {
            var path = uri.LocalPath;
            if (File.Exists(path))
                return Result("SITELIST_URL", "Site list URL reachable", DiagnosticStatus.Passed,
                    "Edge can read the site list from the local file.", path);

            return Result("SITELIST_URL", "Site list URL reachable", DiagnosticStatus.Failed,
                "The configured site list file does not exist. Edge will not receive the site list.",
                path, "Publish the site list from the Applications page, then restart Edge.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(DiagnosticService));
            client.Timeout = TimeSpan.FromSeconds(5);
            using var response = await client.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
                return Result("SITELIST_URL", "Site list URL reachable", DiagnosticStatus.Failed,
                    $"The site list URL responded with HTTP {(int)response.StatusCode}.",
                    uri.ToString(),
                    "Check that the BMPC Site List Service is running (services.msc).");

            return Result("SITELIST_URL", "Site list URL reachable", DiagnosticStatus.Passed,
                "Edge can download the site list from the configured URL.", uri.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result("SITELIST_URL", "Site list URL reachable", DiagnosticStatus.Failed,
                "The site list URL could not be reached. Edge will not receive site-list updates.",
                $"{uri} — {ex.Message}",
                $"Start the '{AppConstants.ServiceDisplayName}' Windows service, then run diagnostics again.");
        }
    }

    private async Task<DiagnosticResult> CheckDatabaseAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(ct);
            var count = await db.Applications.CountAsync(ct);
            return Result("DATABASE", "Configuration database", DiagnosticStatus.Passed,
                $"Database is reachable ({count} registered applications).", AppConstants.DatabasePath);
        }
        catch (Exception ex)
        {
            return Result("DATABASE", "Configuration database", DiagnosticStatus.Failed,
                "The configuration database could not be opened.", ex.Message,
                "Check permissions on " + AppConstants.DatabaseDirectory + " or restore a backup.");
        }
    }

    private Task<DiagnosticResult> CheckRestartPending(CancellationToken ct)
    {
        if (!RestartFlag.IsSet())
            return Task.FromResult(Result("EDGE_RESTART", "Edge restart state", DiagnosticStatus.Passed,
                "No Edge restart is pending."));

        var status = _edgeLauncher.IsEdgeRunning() ? DiagnosticStatus.Warning : DiagnosticStatus.Passed;
        var message = status == DiagnosticStatus.Warning
            ? "Configuration changed but Microsoft Edge has not been restarted. Edge is still using old settings."
            : "A restart flag is set but Edge is not running; settings will apply on next start.";
        return Task.FromResult(Result("EDGE_RESTART", "Edge restart state", status, message,
            action: status == DiagnosticStatus.Warning ? "Close and reopen Microsoft Edge." : null));
    }

    private Task<DiagnosticResult> CheckApplicationRuleAsync(LegacyApplication app, CancellationToken ct)
    {
        if (!app.IsEnabled)
            return Task.FromResult(Result("APP_RULE", "Application site-list rule", DiagnosticStatus.Failed,
                $"'{app.Name}' is disabled, so it is excluded from the site list.",
                action: "Enable the application, then republish the site list."));

        if (!File.Exists(AppConstants.SiteListFilePath))
            return Task.FromResult(Result("APP_RULE", "Application site-list rule", DiagnosticStatus.Failed,
                "No site list has been published, so this application has no IE mode rule.",
                action: "Publish the site list from Setup."));

        try
        {
            var xml = XDocument.Load(AppConstants.SiteListFilePath);
            var found = xml.Root?.Elements("site")
                .Any(s => string.Equals(s.Attribute("url")?.Value, app.SiteListRuleUrl, StringComparison.OrdinalIgnoreCase)) ?? false;
            return Task.FromResult(found
                ? Result("APP_RULE", "Application site-list rule", DiagnosticStatus.Passed,
                    $"Rule '{app.SiteListRuleUrl}' is present in the published site list.")
                : Result("APP_RULE", "Application site-list rule", DiagnosticStatus.Failed,
                    $"Rule '{app.SiteListRuleUrl}' is missing from the published site list.",
                    action: "Republish the site list so it includes this application."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result("APP_RULE", "Application site-list rule", DiagnosticStatus.Failed,
                "The published site list could not be read.", ex.Message));
        }
    }

    private async Task<DiagnosticResult> CheckTrustedSiteAsync(LegacyApplication app, CancellationToken ct)
    {
        if (!UrlNormalizer.TryNormalize(app.BaseUrl, out var uri, out var error) || uri is null)
            return Result("TRUSTED_SITE", "Trusted Sites assignment", DiagnosticStatus.Failed,
                "The application URL is invalid.", error);

        var state = await _zoneService.ReadAsync(uri, ct);
        if (state.CurrentZone == AppConstants.TrustedSitesZone)
            return Result("TRUSTED_SITE", "Trusted Sites assignment", DiagnosticStatus.Passed,
                $"'{uri.Host}' is assigned to the Trusted Sites zone.");

        var status = app.RequiresActiveX ? DiagnosticStatus.Failed : DiagnosticStatus.Warning;
        return Result("TRUSTED_SITE", "Trusted Sites assignment", status,
            $"'{uri.Host}' is in zone '{state.CurrentZoneName}', not Trusted Sites." +
            (state.ManagedByPolicy ? " Zone assignment is managed by organizational policy." : string.Empty),
            action: state.ManagedByPolicy
                ? "Ask the domain administrator to add the site to the Site to Zone Assignment List."
                : "Use Setup > Add to Trusted Sites for this application.");
    }

    private async Task<DiagnosticResult> CheckPopupsAsync(LegacyApplication app, CancellationToken ct)
    {
        if (!UrlNormalizer.TryNormalize(app.BaseUrl, out var uri, out _) || uri is null)
            return Result("POPUPS", "Pop-up allowlist", DiagnosticStatus.NotApplicable,
                "Skipped because the application URL is invalid.");

        var state = await _contentPolicyService.ReadAsync(uri, ct);
        var pattern = uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        var allowed = state.PopupsAllowedForUrls.Contains(pattern, StringComparer.OrdinalIgnoreCase);

        return allowed
            ? Result("POPUPS", "Pop-up allowlist", DiagnosticStatus.Passed,
                $"Pop-ups are allowed for {pattern}.")
            : Result("POPUPS", "Pop-up allowlist", DiagnosticStatus.Warning,
                $"Pop-ups are not allowlisted for {pattern}. Legacy screens that open new windows may be blocked.",
                action: "Use Setup > Allow Pop-ups for this application if its screens open in new windows.");
    }

    private async Task<DiagnosticResult> CheckTrustedZoneDotNetAsync(LegacyApplication app, CancellationToken ct)
    {
        var state = await _zoneService.ReadTrustedZoneDotNetStateAsync(ct);
        if (state.AllConfigured)
            return Result("TRUSTED_DOTNET", "Trusted Sites legacy .NET settings", DiagnosticStatus.Passed,
                "XAML browser applications, XPS documents, Loose XAML, and High Safety manifest permissions are configured.");

        var missing = string.Join(", ", state.Settings.Where(s => !s.IsConfigured).Select(s => s.DisplayName));
        var status = app.RequiresActiveX ? DiagnosticStatus.Failed : DiagnosticStatus.Warning;
        return Result("TRUSTED_DOTNET", "Trusted Sites legacy .NET settings", status,
            $"Required Trusted Sites settings are not configured: {missing}." +
            (state.ManagedByGroupPolicy ? " These settings are managed by organizational policy." : string.Empty),
            action: state.ManagedByGroupPolicy
                ? "Ask the domain administrator to configure the required Trusted Sites URL actions."
                : "Use Setup > Add to Trusted Sites; the required .NET settings are applied automatically.");
    }

    private static DiagnosticResult Result(
        string code, string name, DiagnosticStatus status, string message,
        string? details = null, string? action = null) => new()
    {
        CheckCode = code,
        CheckName = name,
        Status = status,
        FriendlyMessage = message,
        TechnicalDetails = details,
        RecommendedAction = action
    };

    private static async Task CheckSafe(
        List<DiagnosticResult> results,
        Func<CancellationToken, Task<DiagnosticResult>> check,
        CancellationToken ct)
    {
        try
        {
            results.Add(await check(ct));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            results.Add(Result("CHECK_ERROR", "Diagnostic check failed to run", DiagnosticStatus.Warning,
                "One diagnostic check crashed and was skipped.", ex.ToString()));
        }
    }

    private async Task<EdgePolicyState?> CheckSafeWithState(
        List<DiagnosticResult> results,
        Func<CancellationToken, Task<(DiagnosticResult, EdgePolicyState?)>> check,
        CancellationToken ct)
    {
        try
        {
            var (result, state) = await check(ct);
            results.Add(result);
            return state;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            results.Add(Result("CHECK_ERROR", "Diagnostic check failed to run", DiagnosticStatus.Warning,
                "The policy check crashed and was skipped.", ex.ToString()));
            return null;
        }
    }
}
