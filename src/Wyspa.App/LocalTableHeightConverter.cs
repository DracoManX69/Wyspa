using System.Globalization;
using System.Windows.Data;

namespace Wyspa.App;

// Keep the bottom scrollbar reachable when the settings window is short.
public sealed class LocalTableHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Math.Clamp(value is double height ? height - 140 : 250, 160, double.Parse((string)parameter, CultureInfo.InvariantCulture));
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
