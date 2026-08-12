using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace LiveSubtitle.App.Converters;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try { return new BrushConverter().ConvertFromString(value?.ToString() ?? "#FFFFFFFF") ?? Brushes.White; }
        catch { return Brushes.White; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SolidColorBrush brush ? brush.Color.ToString() : "#FFFFFFFF";
}

public sealed class ListeningStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "Listening" : "Not listening";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class FontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        FontWeight.FromOpenTypeWeight(value is int weight ? Math.Clamp(weight, 1, 999) : 600);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is FontWeight weight ? weight.ToOpenTypeWeight() : 600;
}

public sealed class ReduceMotionPopupAnimationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? PopupAnimation.None : PopupAnimation.Fade;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
