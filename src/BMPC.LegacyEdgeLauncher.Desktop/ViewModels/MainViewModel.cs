using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Results;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Desktop.ViewModels;

/// <summary>
/// Shell view model: owns the navigation between pages and the dashboard status header.
/// Standard users see a read-only shell; administrators unlock the management pages.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IEdgePolicyService _policyService;
    private readonly IEdgeLauncher _edgeLauncher;
    private readonly IPrivilegeService _privilegeService;
    private readonly IApplicationRepository _repository;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(
        LauncherViewModel launcher,
        ApplicationsViewModel applications,
        SetupViewModel setup,
        DiagnosticsViewModel diagnostics,
        IEdgePolicyService policyService,
        IEdgeLauncher edgeLauncher,
        IPrivilegeService privilegeService,
        IApplicationRepository repository,
        ILogger<MainViewModel> logger)
    {
        Launcher = launcher;
        Applications = applications;
        Setup = setup;
        Diagnostics = diagnostics;
        _policyService = policyService;
        _edgeLauncher = edgeLauncher;
        _privilegeService = privilegeService;
        _repository = repository;
        _logger = logger;

        IsAdministrator = _privilegeService.IsAdministrator();
        _currentPage = launcher;

        // Keep the dashboard status header in sync when a setup action changes policy/restart state.
        // Fire-and-forget; RefreshStatusAsync handles its own errors internally.
        Setup.StateChanged += (_, _) => _ = RefreshStatusAsync();
        Applications.SiteListPublished += (_, _) => _ = RefreshStatusAsync();
    }

    public LauncherViewModel Launcher { get; }
    public ApplicationsViewModel Applications { get; }
    public SetupViewModel Setup { get; }
    public DiagnosticsViewModel Diagnostics { get; }

    public string ApplicationTitle => $"{AppConstants.ApplicationName}  {AppConstants.ApplicationVersion}";
    public string Organization => AppConstants.Organization;

    public bool IsAdministrator { get; }
    public string ModeLabel => IsAdministrator ? "Administrator mode" : "Standard user mode";

    [ObservableProperty]
    private ObservableObject _currentPage;

    // --- Dashboard status header (section 9.1) ---

    [ObservableProperty]
    private bool _ieModeEnabled;

    [ObservableProperty]
    private string _siteListUrl = "(not configured)";

    [ObservableProperty]
    private bool _restartRequired;

    [ObservableProperty]
    private string _edgeVersion = "Detecting…";

    [ObservableProperty]
    private int _registeredApplications;

    [ObservableProperty]
    private bool _allPoliciesCorrect;

    public async Task InitializeAsync()
    {
        await Launcher.LoadAsync();
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        try
        {
            IsBusy = true;
            EdgeVersion = _edgeLauncher.GetEdgeVersion() ?? "Not found";

            EdgePolicyState state = await _policyService.ReadStateAsync(CancellationToken.None);
            IeModeEnabled = state.IeModeEnabled;
            SiteListUrl = state.SiteListUrl ?? "(not configured)";
            RestartRequired = state.RestartRequired;
            AllPoliciesCorrect = state.AllCorrect;

            var apps = await _repository.GetAllAsync(CancellationToken.None);
            RegisteredApplications = apps.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh dashboard status.");
            StatusMessage = "Could not read Edge policy status: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshConfigurationAsync()
    {
        try
        {
            await Launcher.LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload dashboard applications.");
            StatusMessage = "Could not reload applications: " + ex.Message;
        }

        await RefreshStatusAsync();
    }

    // Navigation is synchronous so the buttons are never gated by an in-flight async command's
    // CanExecute. Each page's data load is kicked off fire-and-forget after the page is shown;
    // the page view models catch their own load errors and surface them via StatusMessage.

    [RelayCommand]
    private void ShowLauncher()
    {
        CurrentPage = Launcher;
        _ = Launcher.LoadAsync();
    }

    [RelayCommand]
    private void ShowApplications()
    {
        CurrentPage = Applications;
        _ = Applications.LoadAsync();
    }

    [RelayCommand]
    private void ShowSetup()
    {
        CurrentPage = Setup;
        _ = Setup.RefreshAsync();
    }

    [RelayCommand]
    private void ShowDiagnostics() => CurrentPage = Diagnostics;
}
