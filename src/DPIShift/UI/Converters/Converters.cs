using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DPIShift.Core.Models;

namespace DPIShift.UI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (Invert)
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ Invert;
}

public sealed class SupportLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush FullBg = Freeze(new SolidColorBrush(Color.FromRgb(23, 38, 30)));
    private static readonly SolidColorBrush PartialBg = Freeze(new SolidColorBrush(Color.FromRgb(40, 32, 20)));
    private static readonly SolidColorBrush UnsupportedBg = Freeze(new SolidColorBrush(Color.FromRgb(35, 35, 40)));

    private static readonly SolidColorBrush FullFg = Freeze(new SolidColorBrush(Color.FromRgb(94, 209, 148)));
    private static readonly SolidColorBrush PartialFg = Freeze(new SolidColorBrush(Color.FromRgb(234, 179, 78)));
    private static readonly SolidColorBrush UnsupportedFg = Freeze(new SolidColorBrush(Color.FromRgb(142, 142, 152)));

    private static readonly SolidColorBrush FullBorder = Freeze(new SolidColorBrush(Color.FromRgb(42, 74, 57)));
    private static readonly SolidColorBrush PartialBorder = Freeze(new SolidColorBrush(Color.FromRgb(79, 61, 33)));
    private static readonly SolidColorBrush UnsupportedBorder = Freeze(new SolidColorBrush(Color.FromRgb(54, 54, 62)));

    public string Mode { get; set; } = "Foreground";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        SupportLevel level = value is SupportLevel s ? s : SupportLevel.Unsupported;
        return Mode switch
        {
            "Background" => level switch
            {
                SupportLevel.Full => FullBg,
                SupportLevel.Partial => PartialBg,
                _ => UnsupportedBg
            },
            "Border" => level switch
            {
                SupportLevel.Full => FullBorder,
                SupportLevel.Partial => PartialBorder,
                _ => UnsupportedBorder
            },
            _ => level switch
            {
                SupportLevel.Full => FullFg,
                SupportLevel.Partial => PartialFg,
                _ => UnsupportedFg
            }
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

public sealed class ValuesEqualConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 2 || values[0] is null || values[1] is null)
            return false;

        if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
            return false;

        return string.Equals(
            System.Convert.ToString(values[0], CultureInfo.InvariantCulture),
            System.Convert.ToString(values[1], CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
