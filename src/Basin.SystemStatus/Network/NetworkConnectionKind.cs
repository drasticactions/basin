namespace Basin.SystemStatus;

/// <summary>
/// The kind of the primary connection that NetworkManager reports.
/// </summary>
public enum NetworkConnectionKind
{
    /// <summary>
    /// There is no primary connection.
    /// </summary>
    None,
    /// <summary>
    /// A wired connection, type <c>802-3-ethernet</c>.
    /// </summary>
    Ethernet,
    /// <summary>
    /// A wireless connection, type <c>802-11-wireless</c>.
    /// </summary>
    Wireless,
    /// <summary>
    /// A VPN or WireGuard connection.
    /// </summary>
    Vpn,
    /// <summary>
    /// Any other connection type.
    /// </summary>
    Other,
}
