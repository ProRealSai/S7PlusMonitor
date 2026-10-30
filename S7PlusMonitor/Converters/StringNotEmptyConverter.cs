using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace S7PlusMonitor.Converters;

public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasText = value is string text && !string.IsNullOrWhiteSpace(text);
        if (targetType == typeof(Visibility) || targetType == typeof(Visibility?))
        {
            return hasText ? Visibility.Visible : Visibility.Collapsed;
        }

        return hasText;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
