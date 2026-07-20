using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Desktop.ViewModels;

/// <summary>
/// Applications management page (section 9.2/9.3). Administrator-only actions guard themselves
/// via <see cref="IsAdministrator"/>; standard users see a read-only list.
/// </summary>
public partial class ApplicationsViewModel : ViewModelBase
{
    private readonly IApplicationRepository _repository;
    private readonly ISiteListGenerator _generator;
    private readonly ISiteListValidator _validator;
    private readonly ISiteListPublisher _publisher;
    private readonly IAuditService _auditService;
    private readonly IPrivilegeService _privilegeService;
    private readonly LegacyApplicationValidator _appValidator = new();
    private readonly ILogger<ApplicationsViewModel> _logger;

    public ApplicationsViewModel(
        IApplicationRepository repository,
        ISiteListGenerator generator,
        ISiteListValidator validator,
        ISiteListPublisher publisher,
        IAuditService auditService,
        IPrivilegeService privilegeService,
        ILogger<ApplicationsViewModel> logger)
    {
        _repository = repository;
        _generator = generator;
        _validator = validator;
        _publisher = publisher;
        _auditService = auditService;
        _privilegeService = privilegeService;
        _logger = logger;
    }

    public bool IsAdministrator => _privilegeService.IsAdministrator();

    /// <summary>Raised after a successful publish so the shell can refresh the restart-pending header.</summary>
    public event EventHandler? SiteListPublished;

    public ObservableCollection<LegacyApplication> Applications { get; } = new();

    public IReadOnlyList<EnvironmentType> Environments { get; } = Enum.GetValues<EnvironmentType>();
    public IReadOnlyList<BrowserOption> Browsers { get; } = new[]
    {
        new BrowserOption(BrowserLaunchMode.EdgeIeMode, "Edge (IE mode) — recommended"),
        new BrowserOption(BrowserLaunchMode.MicrosoftEdge, "Microsoft Edge (normal mode)"),
        new BrowserOption(BrowserLaunchMode.InternetExplorerLegacy, "Internet Explorer (legacy/unsupported)")
    };
    public IReadOnlyList<CompatMode> CompatModes { get; } = Enum.GetValues<CompatMode>();
    public IReadOnlyList<BMPC.LegacyEdgeLauncher.Core.Constants.AppIcons.IconChoice> Icons { get; }
        = BMPC.LegacyEdgeLauncher.Core.Constants.AppIcons.All;

    [ObservableProperty]
    private LegacyApplication? _selectedApplication;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private ApplicationEditModel? _editModel;

    public bool IsEditing => EditModel is not null;

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            var apps = await _repository.GetAllAsync(CancellationToken.None);
            Applications.Clear();
            foreach (var a in apps.OrderBy(a => a.Name))
                Applications.Add(a);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddApplication()
    {
        if (!RequireAdmin()) return;
        EditModel = new ApplicationEditModel();
    }

    [RelayCommand]
    private void EditApplication(LegacyApplication? app)
    {
        if (!RequireAdmin()) return;
        if (app is null) return;
        EditModel = ApplicationEditModel.FromApplication(app);
    }

    [RelayCommand]
    private void CancelEdit() => EditModel = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (EditModel is null || !RequireAdmin()) return;

        if (!UrlNormalizer.TryNormalize(EditModel.BaseUrl, out var uri, out var urlError) || uri is null)
        {
            StatusMessage = urlError;
            return;
        }

        var pathRuleWasInferred = false;
        var normalizedHost = UrlNormalizer.NormalizedHost(uri);
        var hostWideRuleAlreadyExists = Applications.Any(a =>
            a.Id != EditModel.Id &&
            string.Equals(a.NormalizedHost, normalizedHost, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(a.PathRule));

        if (string.IsNullOrWhiteSpace(EditModel.PathRule) && hostWideRuleAlreadyExists)
        {
            var inferredPathRule = UrlNormalizer.InferPathRule(uri);
            if (inferredPathRule is null)
            {
                StatusMessage = $"Another application already uses the whole-host rule '{normalizedHost}'. " +
                                "Enter a distinct path rule for this application.";
                return;
            }

            EditModel.PathRule = inferredPathRule;
            pathRuleWasInferred = true;
        }

        var app = EditModel.ToApplication(uri);
        var validation = _appValidator.Validate(app);
        if (!validation.IsValid)
        {
            StatusMessage = string.Join("  ", validation.Errors.Select(e => e.ErrorMessage));
            return;
        }

        try
        {
            IsBusy = true;
            app.UpdatedBy = System.Environment.UserName;
            var isNew = EditModel.IsNew;
            if (isNew) app.CreatedBy = System.Environment.UserName;

            var result = await _repository.UpsertAsync(app, CancellationToken.None);
            await _auditService.RecordAsync(new AuditEvent
            {
                Action = isNew ? "CreateApplication" : "UpdateApplication",
                EntityType = nameof(LegacyApplication),
                EntityId = app.Id.ToString(),
                NewValueJson = JsonSerializer.Serialize(new { app.Name, app.BaseUrl, Rule = app.SiteListRuleUrl, app.OpenIn, app.LaunchBrowser, app.IsEnabled }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : result.Message
            }, CancellationToken.None);

            if (!result.Succeeded)
            {
                StatusMessage = result.Message;
                return;
            }

            EditModel = null;
            StatusMessage = pathRuleWasInferred
                ? $"Saved with path rule '{app.PathRule}' because '{app.NormalizedHost}' is already registered. " +
                  "Publish the site list to apply this change to Edge."
                : "Saved. Publish the site list to apply this change to Edge.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save application.");
            StatusMessage = "Save failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(LegacyApplication? app)
    {
        if (app is null || !RequireAdmin()) return;

        var confirm = MessageBox.Show(
            $"Delete '{app.Name}'?\n\nThe site list must be republished for the change to reach Edge.",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            IsBusy = true;
            var result = await _repository.DeleteAsync(app.Id, CancellationToken.None);
            await _auditService.RecordAsync(new AuditEvent
            {
                Action = "DeleteApplication",
                EntityType = nameof(LegacyApplication),
                EntityId = app.Id.ToString(),
                OldValueJson = JsonSerializer.Serialize(new { app.Name, app.BaseUrl }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : result.Message
            }, CancellationToken.None);

            StatusMessage = result.Message;
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PublishAsync()
    {
        if (!RequireAdmin()) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Generating and validating the site list…";

            var apps = await _repository.GetAllAsync(CancellationToken.None);
            var neutrals = apps.SelectMany(a => a.NeutralSites).ToList();
            var version = await _repository.GetNextSiteListVersionAsync(CancellationToken.None);
            var document = _generator.Generate(apps, neutrals, version);

            var validation = _validator.Validate(document);
            if (!validation.IsValid)
            {
                StatusMessage = "Site list invalid: " + string.Join("  ", validation.Errors);
                return;
            }

            var result = await _publisher.PublishAsync(document, CancellationToken.None);
            await _auditService.RecordAsync(new AuditEvent
            {
                Action = "PublishSiteList",
                EntityType = "SiteList",
                EntityId = version.ToString(),
                NewValueJson = JsonSerializer.Serialize(new { version, rules = document.Entries.Count, result.Sha256Hash }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : result.Message
            }, CancellationToken.None);

            StatusMessage = result.Succeeded
                ? $"Published site list v{result.Version}. Restart Edge for changes to take effect."
                : result.Message;

            if (result.Succeeded)
                SiteListPublished?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publish failed.");
            StatusMessage = "Publish failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool RequireAdmin()
    {
        if (IsAdministrator) return true;
        StatusMessage = "This action requires administrator mode.";
        return false;
    }
}

/// <summary>Editable form model for the add/edit application dialog (section 9.3).</summary>
public partial class ApplicationEditModel : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool IsNew { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string CreatedBy { get; init; } = System.Environment.UserName;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private EnvironmentType _environment = EnvironmentType.Production;
    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private string? _pathRule;
    [ObservableProperty] private BrowserLaunchMode _launchBrowser = BrowserLaunchMode.EdgeIeMode;
    [ObservableProperty] private CompatMode _compatibilityMode = CompatMode.Default;
    [ObservableProperty] private string? _owner;
    [ObservableProperty] private string? _department;
    [ObservableProperty] private string? _supportContact;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _iconKey = BMPC.LegacyEdgeLauncher.Core.Constants.AppIcons.DefaultKey;
    [ObservableProperty] private string? _description;

    public string Title => IsNew ? "Add application" : "Edit application";

    public static ApplicationEditModel FromApplication(LegacyApplication a) => new()
    {
        Id = a.Id,
        IsNew = false,
        CreatedAt = a.CreatedAt,
        CreatedBy = a.CreatedBy,
        Name = a.Name,
        Environment = a.Environment,
        BaseUrl = a.BaseUrl,
        PathRule = a.PathRule,
        LaunchBrowser = a.LaunchBrowser,
        CompatibilityMode = Enum.TryParse<CompatMode>(a.CompatibilityMode, out var cm) ? cm : CompatMode.Default,
        Owner = a.Owner,
        Department = a.Department,
        SupportContact = a.SupportContact,
        Notes = a.Notes,
        IsEnabled = a.IsEnabled,
        IconKey = string.IsNullOrWhiteSpace(a.IconKey) ? BMPC.LegacyEdgeLauncher.Core.Constants.AppIcons.DefaultKey : a.IconKey,
        Description = a.Description
    };

    public LegacyApplication ToApplication(Uri normalized) => new()
    {
        Id = Id,
        Name = Name.Trim(),
        Environment = Environment,
        BaseUrl = normalized.AbsoluteUri,
        NormalizedHost = UrlNormalizer.NormalizedHost(normalized),
        PathRule = UrlNormalizer.NormalizePathRule(PathRule),
        LaunchBrowser = LaunchBrowser,
        OpenIn = LaunchBrowser == BrowserLaunchMode.MicrosoftEdge
            ? OpenInMode.MSEdge
            : OpenInMode.IE11,
        CompatibilityMode = CompatibilityMode.ToString(),
        Owner = string.IsNullOrWhiteSpace(Owner) ? null : Owner.Trim(),
        Department = string.IsNullOrWhiteSpace(Department) ? null : Department.Trim(),
        SupportContact = string.IsNullOrWhiteSpace(SupportContact) ? null : SupportContact.Trim(),
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        IsEnabled = IsEnabled,
        IconKey = string.IsNullOrWhiteSpace(IconKey) ? BMPC.LegacyEdgeLauncher.Core.Constants.AppIcons.DefaultKey : IconKey,
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy
    };
}

public sealed record BrowserOption(BrowserLaunchMode Value, string DisplayName);
