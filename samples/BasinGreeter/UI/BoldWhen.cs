using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BasinGreeter.UI;

public sealed class BoldWhen : IValueConverter
{
    public static BoldWhen Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
