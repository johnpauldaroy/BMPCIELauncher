using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Results;
using BMPC.LegacyEdgeLauncher.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Desktop.ViewModels;

/// <summary>
/// Policies / first-run setup page (sections 9.4, 47). Administrator-only: applies the Edge
/// IE-mode policies, restarts Edge on confirmation, and configures the NATCCO Trusted Sites
/// and pop-up allowlist entries scoped to the single approved host.
/// </summary>
public partial class SetupViewModel : ViewModelBase
{
    private readonly IEdgePolicyService _policyService;
    private readonly IEdgeLauncher _edgeLauncher;
    private readonly IInternetSecurityZoneService _zoneService;
    private readonly IEdgeContentPolicyService _contentPolicyService;
    private readonly IApplicationRepository _repository;
    private readonly IConfigPortabilityService _configPortability;
    private readonly IAuditService _auditService;
    private readonly IPrivilegeService _privilegeService;
    private readonly ILogger<SetupViewModel> _logger;

    public SetupViewModel(
        IEdgePolicyService policyService,
        IEdgeLauncher edgeLauncher,
        IInternetSecurityZoneService zoneService,
        IEdgeContentPolicyService contentPolicyService,
        IApplicationRepository repository,
        IConfigPortabilityService configPortability,
        IAuditService auditService,
        IPrivilegeService privilegeService,
        ILogger<SetupViewModel> logger)
    {
        _policyService = policyService;
        _edgeLauncher = edgeLauncher;
        _zoneService = zoneService;
        _contentPolicyService = contentPolicyService;
        _repository = repository;
        _configPortability = configPortability;
        _auditService = auditService;
        _privilegeService = privilegeService;
        _logger = logger;
    }

    public bool IsAdministrator => _privilegeService.IsAdministrator();

    /// <summary>Raised after any action that may change policy or restart-pending state,
    /// so the shell can refresh the dashboard status header.</summary>
    public event EventHandler? StateChanged;

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public ObservableCollection<PolicyValueState> Policies { get; } = new();

    [ObservableProperty] private bool _ieModeEnabled;
    [ObservableProperty] private string _siteListUrl = AppConstants.DefaultSiteListUrl;
    [ObservableProperty] private bool _restartRequired;
    [ObservableProperty] private bool _edgeRunning;
    [ObservableProperty] private bool _trustedZoneDotNetConfigured;
    [ObservableProperty] private bool _trustedZoneDotNetManaged;
    [ObservableProperty] private bool _reportAutoOpenConfigured;

    public async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            var state = await _policyService.ReadStateAsync(CancellationToken.None);
            IeModeEnabled = state.IeModeEnabled;
            SiteListUrl = state.SiteListUrl ?? AppConstants.DefaultSiteListUrl;
            RestartRequired = state.RestartRequired;
            EdgeRunning = _edgeLauncher.IsEdgeRunning();
            var dotNetState = await _zoneService.ReadTrustedZoneDotNetStateAsync(CancellationToken.None);
            TrustedZoneDotNetConfigured = dotNetState.AllConfigured;
            TrustedZoneDotNetManaged = dotNetState.ManagedByGroupPolicy;
            ReportAutoOpenConfigured = await _contentPolicyService.IsReportAutoOpenConfiguredAsync(CancellationToken.None);

            Policies.Clear();
            foreach (var v in state.Values)
                Policies.Add(v);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read policy state.");
            StatusMessage = "Could not read Edge policies: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyPoliciesAsync()
    {
        if (!RequireAdmin()) return;
        try
        {
            IsBusy = true;
            var config = new EdgePolicyConfiguration(AppConstants.DefaultSiteListUrl);
            var result = await _policyService.ApplyAsync(config, CancellationToken.None);
            await _auditService.RecordAsync(new AuditEvent
            {
                Action = "ApplyPolicies",
                EntityType = "EdgePolicy",
                NewValueJson = JsonSerializer.Serialize(new { config.SiteListUrl, snapshots = result.SnapshotIds }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : result.Message
            }, CancellationToken.None);

            StatusMessage = result.Message;
            if (result.RestartRequired) RestartRequired = true;
            await RefreshAsync();
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apply policies failed.");
            StatusMessage = "Apply failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TrustNatccoHostAsync()
    {
        if (!RequireAdmin()) return;
        var app = await FindSeededAppAsync();
        if (app is null)
        {
            StatusMessage = "No approved application found to configure.";
            return;
        }
        if (!UrlNormalizer.TryNormalize(app.BaseUrl, out var uri, out var error) || uri is null)
        {
            StatusMessage = error;
            return;
        }

        try
        {
            IsBusy = true;
            var zoneResult = await _zoneService.AddToTrustedSitesAsync(uri, CancellationToken.None);
            var dotNetResult = zoneResult.Succeeded
                ? await _zoneService.ConfigureTrustedZoneDotNetAsync(CancellationToken.None)
                : new TrustedZoneConfigurationResult(false,
                    "Legacy .NET settings were not changed because the Trusted Sites assignment failed.",
                    Array.Empty<Guid>());
            var succeeded = zoneResult.Succeeded && dotNetResult.Succeeded;
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
                ErrorMessage = succeeded ? null : $"{zoneResult.Message} {dotNetResult.Message}"
            }, CancellationToken.None);
            StatusMessage = $"{zoneResult.Message} {dotNetResult.Message}";
            await RefreshAsync();
            NotifyStateChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AllowPopupsAsync()
    {
        if (!RequireAdmin()) return;
        var app = await FindSeededAppAsync();
        if (app is null)
        {
            StatusMessage = "No approved application found to configure.";
            return;
        }
        if (!UrlNormalizer.TryNormalize(app.BaseUrl, out var uri, out var error) || uri is null)
        {
            StatusMessage = error;
            return;
        }

        try
        {
            IsBusy = true;
            var result = await _contentPolicyService.AllowPopupsForUrlAsync(uri, CancellationToken.None);
            await _auditService.RecordAsync(new AuditEvent
            {
                Action = "AllowPopups",
                EntityType = nameof(LegacyApplication),
                EntityId = app.Id.ToString(),
                NewValueJson = JsonSerializer.Serialize(new { uri.Host, result.SnapshotId }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : result.Message
            }, CancellationToken.None);
            StatusMessage = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EnableReportAutoOpenAsync()
    {
        if (!RequireAdmin()) return;

        try
        {
            IsBusy = true;
            var result = await _contentPolicyService.EnableReportAutoOpenAsync(CancellationToken.None);
            await _auditService.RecordAsync(new AuditEvent
            {
                Action = "EnableReportAutoOpen",
                EntityType = "EdgePolicy",
                NewValueJson = JsonSerializer.Serialize(new { extensions = AppConstants.ReportAutoOpenExtensions }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : (result.TechnicalDetails ?? result.Message)
            }, CancellationToken.None);

            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                RestartRequired = true;
                await RefreshAsync();
                NotifyStateChanged();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enable report auto-open failed.");
            StatusMessage = "Failed to enable automatic report opening: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestartEdgeAsync()
    {
        if (!RequireAdmin()) return;

        if (_edgeLauncher.IsEdgeRunning())
        {
            var confirm = MessageBox.Show(
                "Microsoft Edge will be closed so the new configuration takes effect.\n\n" +
                "Unsaved tabs or forms in Edge may be lost. Continue?",
                "Restart Edge", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        try
        {
            IsBusy = true;
            var result = await _edgeLauncher.RestartEdgeAsync(force: false, CancellationToken.None);
            await _auditService.RecordAsync(new AuditEvent
            {
                Action = "RestartEdge",
                NewValueJson = JsonSerializer.Serialize(new { force = false }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : result.Message
            }, CancellationToken.None);
            StatusMessage = result.Message;
            await RefreshAsync();
            NotifyStateChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportConfigAsync()
    {
        if (!RequireAdmin()) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export configuration",
            Filter = "BMPC config (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = $"bmpc-config-{DateTime.Now:yyyyMMdd-HHmm}.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            var result = await _configPortability.ExportAsync(dialog.FileName, CancellationToken.None);
            await _auditService.RecordAsync(new AuditEvent
            {
                Action = "ExportConfig",
                EntityType = "Config",
                NewValueJson = JsonSerializer.Serialize(new { dialog.FileName, result.ApplicationCount }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : (result.TechnicalDetails ?? result.Message)
            }, CancellationToken.None);
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export config failed.");
            StatusMessage = "Export failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        if (!RequireAdmin()) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import configuration",
            Filter = "BMPC config (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            "Import will add new applications and update existing ones that match. " +
            "Nothing will be deleted.\n\nContinue?",
            "Import configuration", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            IsBusy = true;
            var result = await _configPortability.ImportAsync(dialog.FileName, CancellationToken.None);
            await _auditService.RecordAsync(new AuditEvent
            {
                Action = "ImportConfig",
                EntityType = "Config",
                NewValueJson = JsonSerializer.Serialize(new { dialog.FileName, result.Added, result.Updated, result.Skipped }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : (result.TechnicalDetails ?? result.Message)
            }, CancellationToken.None);

            StatusMessage = result.Message;
            if (result.Skipped > 0 && result.TechnicalDetails is not null)
            {
                MessageBox.Show(
                    $"{result.Message}\n\nSkipped items:\n{result.TechnicalDetails}",
                    "Import completed with warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import config failed.");
            StatusMessage = "Import failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<LegacyApplication?> FindSeededAppAsync()
    {
        var apps = await _repository.GetAllAsync(CancellationToken.None);
        return apps.FirstOrDefault(a => a.NormalizedHost == AppConstants.NatccoHost)
               ?? apps.FirstOrDefault(a => a.IsEnabled);
    }

    private bool RequireAdmin()
    {
        if (IsAdministrator) return true;
        StatusMessage = "This action requires administrator mode.";
        return false;
    }
}
