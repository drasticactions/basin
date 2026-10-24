namespace Basin.Cli;

/// <summary>
/// Holds the parsed value of the <c>--transport</c> option.
/// </summary>
/// <param name="Kind">The wire that the Wayland protocol travels on.</param>
public readonly record struct TransportChoice(TransportKind Kind)
{
    /// <summary>
    /// Formats the choice in the spelling that <c>--transport</c> accepts.
    /// </summary>
    /// <returns><c>managed</c> or <c>libwayland</c>.</returns>
    public override string ToString() => Kind == TransportKind.Managed ? "managed" : "libwayland";
}
