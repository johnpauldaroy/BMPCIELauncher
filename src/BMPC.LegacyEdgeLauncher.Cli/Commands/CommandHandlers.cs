using System.Text.Json;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using BMPC.LegacyEdgeLauncher.Core.Validation;

namespace BMPC.LegacyEdgeLauncher.Cli.Commands;

/// <summary>Parses CLI arguments and dispatches to the shared services.</summary>
public class CommandHandlers
{
    private static readonly IReadOnlySet<string> AdministratorCommands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "publish", "apply-policies", "trust", "allow-popups", "restart-edge",
            "backup", "backups", "restore-backup", "rollback-sitelist", "audit"
        };

    private readonly IApplicationRepository _repository;
    private readonly IEdgePolicyService _policyService;
    private readonly ISiteListGenerator _generator;
    private readonly ISiteListPublisher _publisher;
    private readonly IEdgeLauncher _edgeLauncher;
    private readonly IDiagnosticService _diagnosticService;
    private readonly IBackupService _backupService;
    private readonly IAuditService _auditService;
    private readonly IPrivilegeService _privilegeService;
    private readonly IInternetSecurityZoneService _zoneService;
    private readonly IEdgeContentPolicyService _contentPolicyService;

    public CommandHandlers(
        IApplicationRepository repository,
        IEdgePolicyService policyService,
        ISiteListGenerator generator,
        ISiteListPublisher publisher,
        IEdgeLauncher edgeLauncher,
        IDiagnosticService diagnosticService,
        IBackupService backupService,
        IAuditService auditService,
        IPrivilegeService privilegeService,
        IInternetSecurityZoneService zoneService,
        IEdgeContentPolicyService contentPolicyService)
    {
        _repository = repository;
        _policyService = policyService;
        _generator = generator;
        _publisher = publisher;
        _edgeLauncher = edgeLauncher;
        _diagnosticService = diagnosticService;
        _backupService = backupService;
        _auditService = auditService;
        _privilegeService = privilegeService;
        _zoneService = zoneService;
        _contentPolicyService = contentPolicyService;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        // The help text is not an authorization boundary; enforce administrator mode centrally.
        if (AdministratorCommands.Contains(command) && !_privilegeService.IsAdministrator())
        {
            Console.Error.WriteLine($"{command} requires an elevated (administrator) prompt.");
            return 1;
        }

        return command switch
        {
            "status" => await StatusAsync(ct),
            "apps" => await AppsAsync(ct),
            "launch" => await LaunchAsync(rest, ct),
            "publish" => await PublishAsync(ct),
            "apply-policies" => await ApplyPoliciesAsync(rest, ct),
            "trust" => await TrustAsync(rest, ct),
            "allow-popups" => await AllowPopupsAsync(rest, ct),
            "restart-edge" => await RestartEdgeAsync(rest, ct),
            "diagnose" => await DiagnoseAsync(rest, ct),
            "backup" => await BackupAsync(rest, ct),
            "backups" => await BackupsAsync(ct),
            "restore-backup" => await RestoreBackupAsync(rest, ct),
            "rollback-sitelist" => await RollbackSiteListAsync(ct),
            "audit" => await AuditAsync(rest, ct),
            _ => Unknown(command)
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"""
            {AppConstants.ApplicationName} {AppConstants.ApplicationVersion}
            {AppConstants.Organization}

            Usage: bmpc-launcher <command> [options]

            Everyday commands
              status                     Show Edge policy and site-list state
              apps                       List registered legacy applications
              launch <name|id>           Open an application in its configured browser
              diagnose [name|id]         Run the diagnostic checklist

            Administrator commands (run from an elevated prompt)
              apply-policies [--sitelist-url <url>] [--allow-reload]
                                         Apply the Edge IE-mode registry policies
              publish                    Generate, validate, and publish the site list
              trust <name|id>            Configure Trusted Sites and required legacy .NET actions
              allow-popups <name|id>     Allow pop-ups for the app's origin
              restart-edge [--force]     Close Edge so pending changes take effect
              rollback-sitelist          Restore the previous published site list
              backup [reason]            Create a backup set (database + site list)
              backups                    List available backups
              restore-backup <id>        Restore a backup set by id
              audit [--action <name>]    Show recent audit events
            """);
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Run 'bmpc-launcher help' for usage.");
        return 1;
    }

    private async Task<int> StatusAsync(CancellationToken ct)
    {
        var state = await _policyService.ReadStateAsync(ct);
        Console.WriteLine($"IE mode enabled:   {(state.IeModeEnabled ? "yes" : "NO")}");
        Console.WriteLine($"Site list URL:     {state.SiteListUrl ?? "(not configured)"}");
        Console.WriteLine($"Restart pending:   {(state.RestartRequired ? "YES — restart Edge" : "no")}");
        Console.WriteLine($"Administrator:     {(_privilegeService.IsAdministrator() ? "yes" : "no")}");
        Console.WriteLine($"Edge:              {_edgeLauncher.GetEdgeVersion() ?? "NOT FOUND"}");
        Console.WriteLine();
        foreach (var v in state.Values)
            Console.WriteLine($"  {v.ValueName,-45} {v.Status,-18} {v.CurrentValue ?? "(missing)"}");

        var history = await _repository.GetSiteListHistoryAsync(ct);
        var latest = history.FirstOrDefault(h => h.ValidationSucceeded);
        Console.WriteLine();
        Console.WriteLine(latest is null
            ? "Site list:         never published"
            : $"Site list:         v{latest.VersionNumber} published {latest.PublishedAt:g} by {latest.PublishedBy}");
        return state.IeModeEnabled && !state.RestartRequired ? 0 : 1;
    }

    private async Task<int> AppsAsync(CancellationToken ct)
    {
        var apps = await _repository.GetAllAsync(ct);
        if (apps.Count == 0)
        {
            Console.WriteLine("No applications registered.");
            return 0;
        }

        foreach (var app in apps)
        {
            Console.WriteLine($"{(app.IsEnabled ? " " : "!")} {app.Name}");
            Console.WriteLine($"    Id:   {app.Id}");
            Console.WriteLine($"    URL:  {app.BaseUrl}");
            Console.WriteLine($"    Rule: {app.SiteListRuleUrl} → {app.OpenIn}" +
                              (app.IsEnabled ? string.Empty : "  (DISABLED — excluded from site list)"));
            Console.WriteLine($"    Browser: {app.LaunchBrowserDisplayName}");
        }
        return 0;
    }

    private async Task<LegacyApplication?> ResolveAppAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Specify an application name or id.");
            return null;
        }

        var needle = string.Join(' ', args);
        var apps = await _repository.GetAllAsync(ct);

        if (Guid.TryParse(needle, out var id))
        {
            var byId = apps.FirstOrDefault(a => a.Id == id);
            if (byId is not null) return byId;
        }

        var matches = apps.Where(a => a.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        switch (matches.Count)
        {
            case 1:
                return matches[0];
            case 0:
                Console.Error.WriteLine($"No application matches '{needle}'. Run 'bmpc-launcher apps' to list them.");
                return null;
            default:
                Console.Error.WriteLine($"'{needle}' matches {matches.Count} applications — be more specific:");
                foreach (var m in matches) Console.Error.WriteLine($"  {m.Name} ({m.Id})");
                return null;
        }
    }

    private async Task<int> LaunchAsync(string[] args, CancellationToken ct)
    {
        var app = await ResolveAppAsync(args, ct);
        if (app is null) return 1;

        if (!app.IsEnabled)
        {
            Console.Error.WriteLine($"'{app.Name}' is disabled and cannot be launched.");
            return 1;
        }

        if (!UrlNormalizer.TryNormalize(app.BaseUrl, out var uri, out var error) || uri is null)
        {
            Console.Error.WriteLine($"The stored URL is invalid: {error}");
            return 1;
        }

        var result = await _edgeLauncher.OpenAsync(uri, app.LaunchBrowser, ct);
        await _auditService.RecordAsync(new AuditEvent
        {
            Action = "Launch",
            EntityType = nameof(LegacyApplication),
            EntityId = app.Id.ToString(),
            NewValueJson = JsonSerializer.Serialize(new { app.Name, Url = uri.AbsoluteUri, app.LaunchBrowser }),
            Succeeded = result.Succeeded,
            ErrorMessage = result.Succeeded ? null : result.Message
        }, ct);

        return Report(result);
    }

    private async Task<int> PublishAsync(CancellationToken ct)
    {
        var apps = await _repository.GetAllAsync(ct);
        var neutrals = apps.SelectMany(a => a.NeutralSites).ToList();
        var version = await _repository.GetNextSiteListVersionAsync(ct);
        var document = _generator.Generate(apps, neutrals, version);
        var result = await _publisher.PublishAsync(document, ct);

        await _auditService.RecordAsync(new AuditEvent
        {
            Action = "PublishSiteList",
            EntityType = "SiteList",
            EntityId = version.ToString(),
            NewValueJson = JsonSerializer.Serialize(new { version, rules = document.Entries.Count, result.Sha256Hash }),
            Succeeded = result.Succeeded,
            ErrorMessage = result.Succeeded ? null : result.Message
        }, ct);

        return Report(result);
    }

    private async Task<int> ApplyPoliciesAsync(string[] args, CancellationToken ct)
    {
        if (!_privilegeService.IsAdministrator())
        {
            Console.Error.WriteLine("apply-policies requires an elevated (administrator) prompt.");
            return 1;
        }

        var url = AppConstants.DefaultSiteListUrl;
        var allowReload = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sitelist-url" when i + 1 < args.Length:
                    url = args[++i];
                    break;
                case "--allow-reload":
                    allowReload = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                    return 1;
            }
        }

        if (!UrlNormalizer.TryValidateSiteListUrl(url, out var siteListUri, out var validationError))
        {
            Console.Error.WriteLine(validationError);
            return 1;
        }

        var validatedUrl = siteListUri!.AbsoluteUri;
        var result = await _policyService.ApplyAsync(new EdgePolicyConfiguration(validatedUrl, allowReload), ct);
        await _auditService.RecordAsync(new AuditEvent
        {
            Action = "ApplyPolicies",
            EntityType = "EdgePolicy",
            NewValueJson = JsonSerializer.Serialize(new { url = validatedUrl, allowReload, snapshots = result.SnapshotIds }),
            Succeeded = result.Succeeded,
            ErrorMessage = result.Succeeded ? null : result.Message
        }, ct);

        return Report(result);
    }

    private async Task<int> TrustAsync(string[] args, CancellationToken ct)
    {
        var app = await ResolveAppAsync(args, ct);
        if (app is null) return 1;
        if (!UrlNormalizer.TryNormalize(app.BaseUrl, out var uri, out var error) || uri is null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var zoneResult = await _zoneService.AddToTrustedSitesAsync(uri, ct);
        var dotNetResult = zoneResult.Succeeded
            ? await _zoneService.ConfigureTrustedZoneDotNetAsync(ct)
            : new TrustedZoneConfigurationResult(false,
                "Legacy .NET settings were not changed because the Trusted Sites assignment failed.",
                Array.Empty<Guid>());
        var succeeded = zoneResult.Succeeded && dotNetResult.Succeeded;
        var result = new OperationResult(succeeded, $"{zoneResult.Message} {dotNetResult.Message}");
        await _auditService.RecordAsync(new AuditEvent
        {
            Action = "ConfigureTrustedSite",
            EntityType = nameof(LegacyApplication),
            EntityId = app.Id.ToString(),
            NewValueJson = JsonSerializer.Serialize(new
            {
                uri.Host,
                zoneSnapshot = zoneResult.SnapshotId,
                dotNetSnapshots = dotNetResult.SnapshotIds
            }),
            Succeeded = succeeded,
            ErrorMessage = succeeded ? null : result.Message
        }, ct);
        return Report(result);
    }

    private async Task<int> AllowPopupsAsync(string[] args, CancellationToken ct)
    {
        if (!_privilegeService.IsAdministrator())
        {
            Console.Error.WriteLine("allow-popups requires an elevated (administrator) prompt.");
            return 1;
        }

        var app = await ResolveAppAsync(args, ct);
        if (app is null) return 1;
        if (!UrlNormalizer.TryNormalize(app.BaseUrl, out var uri, out var error) || uri is null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var result = await _contentPolicyService.AllowPopupsForUrlAsync(uri, ct);
        await _auditService.RecordAsync(new AuditEvent
        {
            Action = "AllowPopups",
            EntityType = nameof(LegacyApplication),
            EntityId = app.Id.ToString(),
            NewValueJson = JsonSerializer.Serialize(new { uri.Host, result.SnapshotId }),
            Succeeded = result.Succeeded,
            ErrorMessage = result.Succeeded ? null : result.Message
        }, ct);
        return Report(result);
    }

    private async Task<int> RestartEdgeAsync(string[] args, CancellationToken ct)
    {
        var force = args.Contains("--force");
        var result = await _edgeLauncher.RestartEdgeAsync(force, ct);
        await _auditService.RecordAsync(new AuditEvent
        {
            Action = "RestartEdge",
            NewValueJson = JsonSerializer.Serialize(new { force }),
            Succeeded = result.Succeeded,
            ErrorMessage = result.Succeeded ? null : result.Message
        }, ct);
        return Report(result);
    }

    private async Task<int> DiagnoseAsync(string[] args, CancellationToken ct)
    {
        Guid? appId = null;
        if (args.Length > 0)
        {
            var app = await ResolveAppAsync(args, ct);
            if (app is null) return 1;
            appId = app.Id;
        }

        Console.WriteLine("Running diagnostics...");
        var report = await _diagnosticService.RunAsync(appId, ct);

        Console.WriteLine();
        foreach (var r in report.Results)
        {
            var tag = r.Status switch
            {
                DiagnosticStatus.Passed => "[ OK ]",
                DiagnosticStatus.Warning => "[WARN]",
                DiagnosticStatus.Failed => "[FAIL]",
                _ => "[ -- ]"
            };
            Console.WriteLine($"{tag} {r.CheckName}: {r.FriendlyMessage}");
            if (r.RecommendedAction is not null)
                Console.WriteLine($"       → {r.RecommendedAction}");
        }

        Console.WriteLine();
        Console.WriteLine($"Overall: {report.OverallStatus}   (support reference: {report.CorrelationId})");
        return report.OverallStatus == DiagnosticStatus.Failed ? 1 : 0;
    }

    private async Task<int> BackupAsync(string[] args, CancellationToken ct)
    {
        var reason = args.Length > 0 ? string.Join(' ', args) : "Manual backup from CLI";
        var result = await _backupService.CreateAsync(reason, ct);
        await _auditService.RecordAsync(new AuditEvent
        {
            Action = "CreateBackup",
            EntityType = "Backup",
            EntityId = result.BackupId?.ToString(),
            NewValueJson = JsonSerializer.Serialize(new { reason, result.BackupPath }),
            Succeeded = result.Succeeded,
            ErrorMessage = result.Succeeded ? null : result.Message
        }, ct);
        if (result.Succeeded)
            Console.WriteLine($"Backup id: {result.BackupId}\nLocation:  {result.BackupPath}");
        return Report(result);
    }

    private async Task<int> BackupsAsync(CancellationToken ct)
    {
        var backups = await _backupService.ListAsync(ct);
        if (backups.Count == 0)
        {
            Console.WriteLine("No backups found.");
            return 0;
        }
        foreach (var b in backups)
            Console.WriteLine($"{b.Id}  {b.CreatedAt:g}  by {b.CreatedBy}  — {b.Reason}");
        return 0;
    }

    private async Task<int> RestoreBackupAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || !Guid.TryParse(args[0], out var id))
        {
            Console.Error.WriteLine("Specify the backup id (from 'bmpc-launcher backups').");
            return 1;
        }

        var result = await _backupService.RestoreAsync(id, ct);
        await _auditService.RecordAsync(new AuditEvent
        {
            Action = "RestoreBackup",
            EntityType = "Backup",
            EntityId = id.ToString(),
            Succeeded = result.Succeeded,
            ErrorMessage = result.Succeeded ? null : result.Message
        }, ct);
        if (result.Succeeded && result.EdgeRestartRequired)
            Console.WriteLine("Restart Microsoft Edge to pick up the restored site list.");
        return Report(result);
    }

    private async Task<int> RollbackSiteListAsync(CancellationToken ct)
    {
        var result = await _publisher.RollbackAsync(ct);
        await _auditService.RecordAsync(new AuditEvent
        {
            Action = "RollbackSiteList",
            EntityType = "SiteList",
            Succeeded = result.Succeeded,
            ErrorMessage = result.Succeeded ? null : result.Message
        }, ct);
        return Report(result);
    }

    private async Task<int> AuditAsync(string[] args, CancellationToken ct)
    {
        string? action = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--action" && i + 1 < args.Length)
                action = args[++i];
        }

        var events = await _auditService.QueryAsync(DateTimeOffset.Now.AddDays(-30), null, action, ct);
        if (events.Count == 0)
        {
            Console.WriteLine("No audit events in the last 30 days.");
            return 0;
        }

        foreach (var e in events.Take(50))
        {
            var outcome = e.Succeeded ? "ok" : "FAILED";
            Console.WriteLine($"{e.Timestamp:g}  {e.Username,-15} {e.Action,-20} {outcome}" +
                              (e.ErrorMessage is null ? string.Empty : $"  {e.ErrorMessage}"));
        }
        return 0;
    }

    private int Report(OperationResult result)
    {
        if (result.Succeeded)
        {
            Console.WriteLine(result.Message);
            return 0;
        }

        Console.Error.WriteLine(result.Message);
        var diagnosticDetailsEnabled =
            Environment.GetEnvironmentVariable("BMPC_LAUNCHER_DIAGNOSTIC_DETAILS") == "1";
        if (result.TechnicalDetails is not null && diagnosticDetailsEnabled && _privilegeService.IsAdministrator())
            Console.Error.WriteLine($"Details: {result.TechnicalDetails}");
        return 1;
    }
}
