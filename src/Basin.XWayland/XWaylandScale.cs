namespace Basin.XWayland;

public readonly record struct XWaylandScale
{
    private XWaylandScale(double value) => Value = value;

    public static XWaylandScale Auto => default;

    public static XWaylandScale Fixed(double value)
    {
        if (!double.IsFinite(value) || value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "an X11 scale must be finite and at least 1");
        }

        return new(value);
    }

    public bool IsAuto => Value == 0;

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

    public double Value { get; }

    public override string ToString() => IsAuto ? "auto" : Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
