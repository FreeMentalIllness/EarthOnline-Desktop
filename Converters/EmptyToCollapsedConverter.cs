using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EarthOnline.Desktop;

/// <summary>空字符串 / null → Collapsed，否则 Visible（用于「签名」等可选文本）。</summary>
public class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
