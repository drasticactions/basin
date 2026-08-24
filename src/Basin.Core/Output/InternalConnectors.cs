namespace Basin;

public static class InternalConnectors
{
    private static readonly string[] Prefixes = ["eDP", "LVDS", "DSI"];

    public static bool IsInternal(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Prefixes.Any(prefix => output.Name.StartsWith(prefix, StringComparison.Ordinal));
    }
}
