using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// Converts a value that tells whether a prompt allows more than one selection into a list <see cref="SelectionMode"/>.
/// </summary>
public sealed class SelectionModeConverter : IValueConverter
{
    /// <summary>
    /// Gets the shared instance for use in XAML.
    /// </summary>
    public static SelectionModeConverter Instance { get; } = new();

    /// <summary>
    /// Converts a multiple-selection flag into a selection mode.
    /// </summary>
    /// <param name="value"><see langword="true"/> to allow more than one selection.</param>
    /// <param name="targetType">The target type. It is ignored.</param>
    /// <param name="parameter">The converter parameter. It is ignored.</param>
    /// <param name="culture">The culture. It is ignored.</param>
    /// <returns><see cref="SelectionMode.Multiple"/> with <see cref="SelectionMode.Toggle"/> for <see langword="true"/>, else <see cref="SelectionMode.Single"/>.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? SelectionMode.Multiple | SelectionMode.Toggle : SelectionMode.Single;

    /// <summary>
    /// Converts a selection mode back into a multiple-selection flag.
    /// </summary>
    /// <param name="value">The selection mode.</param>
    /// <param name="targetType">The target type. It is ignored.</param>
    /// <param name="parameter">The converter parameter. It is ignored.</param>
    /// <param name="culture">The culture. It is ignored.</param>
    /// <returns><see langword="true"/> when the mode includes <see cref="SelectionMode.Multiple"/>.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SelectionMode mode && (mode & SelectionMode.Multiple) != 0;
}
