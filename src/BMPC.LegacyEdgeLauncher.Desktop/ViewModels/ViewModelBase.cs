using CommunityToolkit.Mvvm.ComponentModel;

namespace BMPC.LegacyEdgeLauncher.Desktop.ViewModels;

/// <summary>Shared base carrying busy state and a status message for all page view models.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;
}
