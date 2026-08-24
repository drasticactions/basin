namespace Basin.Desktop;

public static class PrivilegedProtocols
{
    public static bool Contains(string interfaceName) => Basin.PrivilegedProtocols.Contains(interfaceName);

    public static IReadOnlyCollection<string> All => Basin.PrivilegedProtocols.All;
}
