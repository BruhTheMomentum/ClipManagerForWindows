using System;
using System.Globalization;
using System.Windows.Data;

namespace ClipManagerForWindows.Converters;

public class IndexToShortcutConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int index && index >= 0 && index < 3)
            return $"Ctrl+{index + 1}";
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
