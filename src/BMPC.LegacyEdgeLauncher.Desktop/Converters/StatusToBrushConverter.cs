using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BMPC.LegacyEdgeLauncher.Core.Enums;

namespace BMPC.LegacyEdgeLauncher.Desktop.Converters;

/// <summary>Maps a DiagnosticStatus (or its string form) to a traffic-light brush.</summary>
public class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value switch
        {
            DiagnosticStatus s => s,
            string text when Enum.TryParse<DiagnosticStatus>(text, out var parsed) => parsed,
            _ => (DiagnosticStatus?)null
        };

        return status switch
        {
            DiagnosticStatus.Passed => Green,
            DiagnosticStatus.Warning => Amber,
            DiagnosticStatus.Failed => Red,
            _ => Gray
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>true → green, false → red. Used for success flags in lists.</summary>
public class BoolToOkBrushConverter : IValueConverter
{
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Green : Red;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
