namespace Basin.SystemStatus;

/// <summary>
/// One wireless network that <see cref="NetworkStatus"/> lists. Each SSID has one entry: the active access point, else the strongest one.
/// </summary>
/// <param name="Path">The D-Bus object path of the access point.</param>
/// <param name="Ssid">The network name, decoded as UTF-8. It is never empty, because the reader drops hidden networks.</param>
/// <param name="Strength">The signal strength from 0 to 100.</param>
/// <param name="Security">The security from the flags of the access point.</param>
/// <param name="IsActive">A value indicating whether this is the active access point of the wireless device.</param>
/// <param name="Saved">The saved wireless connection with the same SSID, or <see langword="null"/>.</param>
public sealed record NetworkAccessPoint(string Path, string Ssid, byte Strength, AccessPointSecurity Security, bool IsActive, NetworkSavedConnection? Saved);
