using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace S7PlusMonitor.Converters;

public sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isZero = value switch
        {
            int count => count == 0,
            long count => count == 0,
            _ => true
        };
        return isZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
