using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using BMPC.LegacyEdgeLauncher.Core.Constants;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BMPC.LegacyEdgeLauncher.Desktop.ViewModels;

/// <summary>Dashboard tile list. Every user (standard or admin) can launch enabled approved systems.</summary>
public partial class LauncherViewModel : ViewModelBase
{
    private readonly IApplicationRepository _repository;
    private readonly IEdgeLauncher _edgeLauncher;
    private readonly IAuditService _auditService;
    private readonly Services.TileOrderStore _tileOrder;
    private readonly ILogger<LauncherViewModel> _logger;

    public LauncherViewModel(
        IApplicationRepository repository,
        IEdgeLauncher edgeLauncher,
        IAuditService auditService,
        Services.TileOrderStore tileOrder,
        ILogger<LauncherViewModel> logger)
    {
        _repository = repository;
        _edgeLauncher = edgeLauncher;
        _auditService = auditService;
        _tileOrder = tileOrder;
        _logger = logger;
    }

    public ObservableCollection<AppTileViewModel> Tiles { get; } = new();

    [ObservableProperty]
    private bool _hasNoApplications;

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            var apps = await _repository.GetAllAsync(CancellationToken.None);

            // Apply this user's saved arrangement: known ids first in their saved order,
            // then any application not yet in the saved order, alphabetically (e.g. newly added).
            var savedOrder = _tileOrder.Load();
            var rank = savedOrder
                .Select((id, index) => (id, index))
                .ToDictionary(x => x.id, x => x.index);

            var ordered = apps
                .OrderBy(a => rank.TryGetValue(a.Id, out var i) ? i : int.MaxValue)
                .ThenBy(a => a.Name);

            Tiles.Clear();
            foreach (var app in ordered)
                Tiles.Add(new AppTileViewModel(app));
            HasNoApplications = Tiles.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Moves the tile at <paramref name="fromIndex"/> so it sits at <paramref name="toIndex"/>,
    /// then persists the new arrangement for this user. Called by the dashboard's drag-and-drop.
    /// </summary>
    public void MoveTile(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Tiles.Count) return;
        if (toIndex < 0 || toIndex >= Tiles.Count) return;
        if (fromIndex == toIndex) return;

        Tiles.Move(fromIndex, toIndex);
        _tileOrder.Save(Tiles.Select(t => t.Id));
    }

    [RelayCommand]
    private async Task LaunchAsync(AppTileViewModel? tile)
    {
        if (tile is null) return;

        if (!tile.IsEnabled)
        {
            StatusMessage = $"'{tile.Name}' is disabled and cannot be launched.";
            return;
        }

        // Final safety validation immediately before launch (section 14.1).
        if (!UrlNormalizer.TryNormalize(tile.BaseUrl, out var uri, out var error) || uri is null)
        {
            StatusMessage = $"The stored URL is invalid: {error}";
            return;
        }

        if (!UrlNormalizer.IsSafeToLaunch(uri, out var reason))
        {
            StatusMessage = $"Launch blocked: {reason}";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Opening {tile.Name} in {tile.Browser}…";
            var result = await _edgeLauncher.OpenAsync(uri, tile.LaunchBrowser, CancellationToken.None, tile.CompatibilityMode);

            await _auditService.RecordAsync(new AuditEvent
            {
                Action = "Launch",
                EntityType = nameof(LegacyApplication),
                EntityId = tile.Id.ToString(),
                NewValueJson = JsonSerializer.Serialize(new { tile.Name, Url = uri.AbsoluteUri, tile.LaunchBrowser }),
                Succeeded = result.Succeeded,
                ErrorMessage = result.Succeeded ? null : result.Message
            }, CancellationToken.None);

            StatusMessage = result.Message;
            if (!result.Succeeded && result.BlockedReason is not null)
            {
                MessageBox.Show(result.Message + "\n\n" + result.BlockedReason,
                    "Launch blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Launch failed for {App}.", tile.Name);
            StatusMessage = "Launch failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Read-only projection of a registered application for the dashboard tile.</summary>
public partial class AppTileViewModel : ObservableObject
{
    public AppTileViewModel(LegacyApplication app)
    {
        Id = app.Id;
        Name = app.Name;
        Environment = app.Environment.ToString();
        BaseUrl = app.BaseUrl;
        LaunchBrowser = app.LaunchBrowser;
        Browser = app.LaunchBrowserDisplayName;
        CompatibilityMode = app.CompatibilityMode;
        IsEnabled = app.IsEnabled;
        LastTested = app.LastTestedAt is { } t ? t.LocalDateTime.ToString("g") : "Never tested";
        SupportContact = app.SupportContact;

        // Prefer the admin-configured icon/description; fall back to name-based inference
        // only for legacy rows saved before those fields existed.
        var inferred = DeriveIconAndDescription(app.Name, BaseUrl);
        Icon = AppIcons.GlyphFor(string.IsNullOrWhiteSpace(app.IconKey) ? inferred.IconKey : app.IconKey);
        Description = string.IsNullOrWhiteSpace(app.Description) ? inferred.Description : app.Description!;
    }

    /// <summary>
    /// Best-effort icon key + subtitle guessed from the application name, used only as a
    /// fallback for applications that predate the configurable icon/description fields.
    /// </summary>
    private static (string IconKey, string Description) DeriveIconAndDescription(string name, string baseUrl)
    {
        var haystack = $"{name} {baseUrl}".ToLowerInvariant();

        if (haystack.Contains("loan") || haystack.Contains("ekoop"))
            return ("Loans", "Loan interface.");
        if (haystack.Contains("account"))
            return ("Accounting", "Accounting tools.");
        if (haystack.Contains("web"))
            return ("Web", "Web portal.");
        if (haystack.Contains("casa") || haystack.Contains("core") || haystack.Contains("bank") || haystack.Contains("logon"))
            return ("Bank", "Core banking portal.");
        if (haystack.Contains("util") || haystack.Contains("tool") || haystack.Contains("colisap") || haystack.Contains("admin"))
            return ("Utilities", "System utilities.");
        return (AppIcons.DefaultKey, "Approved system.");
    }

    public Guid Id { get; }
    public string Name { get; }
    public string Environment { get; }
    public string BaseUrl { get; }
    public BMPC.LegacyEdgeLauncher.Core.Enums.BrowserLaunchMode LaunchBrowser { get; }
    public string Browser { get; }
    public string CompatibilityMode { get; }
    public bool IsEnabled { get; }
    public string LastTested { get; }
    public string? SupportContact { get; }

    /// <summary>Segoe MDL2 Assets glyph shown at the top of the dashboard card.</summary>
    public string Icon { get; }

    /// <summary>Short one-line subtitle describing the system.</summary>
    public string Description { get; }

    public string StatusText => IsEnabled ? "Enabled" : "Disabled";
}
