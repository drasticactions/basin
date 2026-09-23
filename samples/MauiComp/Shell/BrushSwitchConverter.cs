using System.Globalization;
using Microsoft.Maui.Controls;

namespace MauiComp.Shell;

public sealed class BrushSwitchConverter : IValueConverter
{
    public string TrueKey { get; set; } = string.Empty;

    public string FalseKey { get; set; } = string.Empty;

    public bool Frosted { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        RoyaleTheme.Brush((value is true ? TrueKey : FalseKey) + (Frosted ? "Frost" : string.Empty));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
