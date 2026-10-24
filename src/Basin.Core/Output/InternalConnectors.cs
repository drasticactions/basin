namespace Basin;

/// <summary>
/// Recognizes the connectors of a built-in panel by name.
/// </summary>
public static class InternalConnectors
{
    private static readonly string[] Prefixes = ["eDP", "LVDS", "DSI"];

    /// <summary>
    /// Tells whether an output is a built-in panel. The test is a name that starts with <c>eDP</c>, <c>LVDS</c> or <c>DSI</c>.
    /// </summary>
    /// <param name="output">The output to test.</param>
    /// <returns><see langword="true"/> for an internal connector.</returns>
    public static bool IsInternal(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Prefixes.Any(prefix => output.Name.StartsWith(prefix, StringComparison.Ordinal));
    }
}
