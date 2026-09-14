using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Basin.Portal.Prompts.Avalonia;

public sealed class SelectionModeConverter : IValueConverter
{
    public static SelectionModeConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? SelectionMode.Multiple | SelectionMode.Toggle : SelectionMode.Single;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SelectionMode mode && (mode & SelectionMode.Multiple) != 0;
}
