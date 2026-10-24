namespace Basin.SystemStatus;

/// <summary>
/// The security of a wireless network, from the flags of its access point.
/// </summary>
public enum AccessPointSecurity
{
    /// <summary>
    /// The network asks for no password. This includes an OWE network.
    /// </summary>
    Open,
    /// <summary>
    /// The network sets the privacy flag with no WPA or RSN flags.
    /// </summary>
    Wep,
    /// <summary>
    /// WPA personal with a pre-shared key, or WPA flags with no known key management.
    /// </summary>
    Wpa,

    /// <summary>
    /// WPA3 personal with SAE, also a transition network that offers SAE and PSK.
    /// </summary>
    Wpa3,
    /// <summary>
    /// 802.1X or Suite B. <see cref="NetworkStatus"/> cannot join such a network without a saved connection.
    /// </summary>
    Enterprise,
}
