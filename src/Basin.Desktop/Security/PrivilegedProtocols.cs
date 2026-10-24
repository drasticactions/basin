namespace Basin.Desktop;

/// <summary>
/// Forwards to the core list of protocol interfaces that a sandboxed client must not see.
/// </summary>
public static class PrivilegedProtocols
{
    /// <summary>
    /// Tests whether an interface is privileged.
    /// </summary>
    /// <param name="interfaceName">The Wayland interface name, for example <c>zwlr_screencopy_manager_v1</c>.</param>
    /// <returns><see langword="true"/> when the interface is in the privileged list.</returns>
    public static bool Contains(string interfaceName) => Basin.PrivilegedProtocols.Contains(interfaceName);

    /// <summary>
    /// Gets every privileged interface name.
    /// </summary>
    public static IReadOnlyCollection<string> All => Basin.PrivilegedProtocols.All;
}
