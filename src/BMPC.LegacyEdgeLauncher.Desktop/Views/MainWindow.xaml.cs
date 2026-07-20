using System.Windows;
using BMPC.LegacyEdgeLauncher.Desktop.ViewModels;

namespace BMPC.LegacyEdgeLauncher.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ApplicationsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SettingsToggle.IsChecked = false;
        if (!Services.SettingsGate.Authenticate(this)) return;

        if (DataContext is MainViewModel viewModel)
        {
            ShowManagementDialog("Applications", viewModel.Applications,
                () => viewModel.Applications.LoadAsync());
        }
    }

    private void SetupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SettingsToggle.IsChecked = false;
        if (!Services.SettingsGate.Authenticate(this)) return;

        if (DataContext is MainViewModel viewModel)
        {
            ShowManagementDialog("Policies & Setup", viewModel.Setup,
                () => viewModel.Setup.RefreshAsync());
        }
    }

    private void DiagnosticsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SettingsToggle.IsChecked = false;
        if (!Services.SettingsGate.Authenticate(this)) return;

        if (DataContext is MainViewModel viewModel)
        {
            ShowManagementDialog("Diagnostics", viewModel.Diagnostics);
        }
    }

    private void ShowManagementDialog(string title, object content, Func<Task>? loadAsync = null)
    {
        var dialog = new Window
        {
            Title = $"{title} - BMPC IE Launcher",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = Math.Max(800, Math.Min(ActualWidth - 80, 1100)),
            Height = Math.Max(520, Math.Min(ActualHeight - 80, 760)),
            MinWidth = 800,
            MinHeight = 520,
            Background = (System.Windows.Media.Brush)FindResource(SystemColors.ControlBrushKey),
            Content = content,
            Icon = Icon,
            ShowInTaskbar = false
        };

        if (loadAsync is not null)
        {
            dialog.Loaded += async (_, _) => await loadAsync();
        }

        dialog.ShowDialog();
    }
}
