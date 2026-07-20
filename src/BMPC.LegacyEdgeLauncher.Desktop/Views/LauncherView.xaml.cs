using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BMPC.LegacyEdgeLauncher.Desktop.ViewModels;

namespace BMPC.LegacyEdgeLauncher.Desktop.Views;

public partial class LauncherView : UserControl
{
    private Point _pressPoint;
    private AppTileViewModel? _pressTile;
    private bool _dragging;

    // Remembers the tile currently highlighted as a drop target so we can clear it.
    private Border? _highlighted;
    private static readonly Brush DropHighlight = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly Brush NormalBorder = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));

    public LauncherView() => InitializeComponent();

    private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ignore presses that start on an interactive control (the OPEN SYSTEM link) so
        // clicking it still launches instead of beginning a drag.
        if (IsWithinButton(e.OriginalSource as DependencyObject))
        {
            _pressTile = null;
            return;
        }

        if (sender is Border { DataContext: AppTileViewModel tile })
        {
            _pressPoint = e.GetPosition(null);
            _pressTile = tile;
            _dragging = false;
        }
    }

    private void Tile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A press that ended without a drag starting is a click: launch the app.
        // (Presses on the OPEN SYSTEM button cleared _pressTile, so its own command
        // fires normally and we don't double-launch here.)
        var tile = _pressTile;
        _pressTile = null;
        if (tile is null || _dragging) return;

        if (DataContext is LauncherViewModel vm && vm.LaunchCommand.CanExecute(tile))
            vm.LaunchCommand.Execute(tile);
    }

    private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressTile is null || _dragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragging = true;
        try
        {
            DragDrop.DoDragDrop((Border)sender, _pressTile, DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
            _pressTile = null;
            ClearHighlight();
        }
    }

    private void Tile_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(AppTileViewModel)) &&
            sender is Border border &&
            !ReferenceEquals(border.DataContext, e.Data.GetData(typeof(AppTileViewModel))))
        {
            e.Effects = DragDropEffects.Move;
            Highlight(border);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Tile_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border && ReferenceEquals(border, _highlighted))
            ClearHighlight();
    }

    private void Tile_Drop(object sender, DragEventArgs e)
    {
        ClearHighlight();

        if (!e.Data.GetDataPresent(typeof(AppTileViewModel))) return;
        if (e.Data.GetData(typeof(AppTileViewModel)) is not AppTileViewModel source) return;
        if (sender is not Border { DataContext: AppTileViewModel target }) return;
        if (DataContext is not LauncherViewModel vm) return;

        var from = vm.Tiles.IndexOf(source);
        var to = vm.Tiles.IndexOf(target);
        vm.MoveTile(from, to);
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void Highlight(Border border)
    {
        if (ReferenceEquals(border, _highlighted)) return;
        ClearHighlight();
        _highlighted = border;
        border.BorderBrush = DropHighlight;
        border.BorderThickness = new Thickness(2);
    }

    private void ClearHighlight()
    {
        if (_highlighted is null) return;
        _highlighted.BorderBrush = NormalBorder;
        _highlighted.BorderThickness = new Thickness(1);
        _highlighted = null;
    }
}
