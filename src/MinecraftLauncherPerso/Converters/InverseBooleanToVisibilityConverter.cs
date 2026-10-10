using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MinecraftLauncherPerso.Converters;

/// <summary>Vrai → Collapsed, faux → Visible (l'inverse de BooleanToVisibilityConverter).</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}
