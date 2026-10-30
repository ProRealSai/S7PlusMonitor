using System.Globalization;
using System.Windows.Data;

namespace S7PlusMonitor.Converters;

public sealed class TimeSpanToDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimeSpan span)
        {
            return DateTime.Today.Add(span);
        }

        return DateTime.Today;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
        {
            return dateTime.TimeOfDay;
        }

        return TimeSpan.Zero;
    }
}
