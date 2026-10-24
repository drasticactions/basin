namespace Basin.XWayland;

/// <summary>
/// The scale setting of an Xwayland server: automatic, or a fixed value of at least 1.
/// </summary>
public readonly record struct XWaylandScale
{
    private XWaylandScale(double value) => Value = value;

    /// <summary>
    /// Gets the automatic scale, which follows the largest output scale.
    /// </summary>
    public static XWaylandScale Auto => default;

    /// <summary>
    /// Creates a fixed scale.
    /// </summary>
    /// <param name="value">The scale. It must be finite and at least 1.</param>
    /// <returns>The fixed scale.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not finite or is less than 1.</exception>
    public static XWaylandScale Fixed(double value)
    {
        if (!double.IsFinite(value) || value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "an X11 scale must be finite and at least 1");
        }

        return new(value);
    }

    /// <summary>
    /// Gets a value indicating whether the scale is automatic.
    /// </summary>
    public bool IsAuto => Value == 0;

    /// <summary>
    /// Reads a scale from a configuration value. <see langword="null"/>, an empty string and <c>auto</c> give <see cref="Auto"/>.
    /// </summary>
    /// <param name="value">A string, a <see cref="double"/> or a <see cref="long"/>.</param>
    /// <param name="scale">The parsed scale, or <see cref="Auto"/> when the parse fails.</param>
    /// <returns><see langword="false"/> when the value is not a number of at least 1 or a recognized automatic value.</returns>
    public static bool TryParse(object? value, out XWaylandScale scale)
    {
        scale = Auto;
        switch (value)
        {
            case null:
            case string { Length: 0 }:
                return true;
            case string text when string.Equals(text, "auto", StringComparison.Ordinal):
                return true;
            case string text when double.TryParse(
                text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                && double.IsFinite(parsed) && parsed >= 1:
                scale = new(parsed);
                return true;
            case double real when double.IsFinite(real) && real >= 1:
                scale = new(real);
                return true;
            case long whole when whole >= 1:
                scale = new(whole);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Gets the fixed scale, or 0 for <see cref="Auto"/>.
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Formats the scale as <see cref="TryParse"/> reads it.
    /// </summary>
    /// <returns><c>auto</c>, or the value in the invariant culture.</returns>
    public override string ToString() => IsAuto ? "auto" : Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
