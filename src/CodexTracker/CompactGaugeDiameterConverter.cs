using System.Globalization;
using System.Windows.Data;
using CodexTracker.Core;

namespace CodexTracker;

public sealed class CompactGaugeDiameterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var dimension = value is double actualDimension ? actualDimension : 0d;
        if (string.Equals(parameter?.ToString(), "FontSize", StringComparison.OrdinalIgnoreCase))
            return CompactGaugeLayoutPolicy.FontSizeForWindow(new WidgetSize(dimension, 0d));

        var layout = CompactGaugeLayoutPolicy.ForWindow(new WidgetSize(0d, dimension));
        return string.Equals(parameter?.ToString(), "Background", StringComparison.OrdinalIgnoreCase)
            ? layout.BackgroundDiameter
            : layout.GaugeDiameter;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
