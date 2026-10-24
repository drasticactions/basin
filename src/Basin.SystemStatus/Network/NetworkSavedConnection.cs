namespace Basin.SystemStatus;

/// <summary>
/// One connection profile that NetworkManager has saved.
/// </summary>
/// <param name="Path">The D-Bus object path of the settings connection.</param>
/// <param name="Id">The <c>connection.id</c>, or an empty string when the profile has none.</param>
/// <param name="Type">The <c>connection.type</c>, for example <c>802-11-wireless</c>.</param>
/// <param name="Ssid">The SSID of a wireless profile, decoded as UTF-8, or <see langword="null"/>.</param>
public sealed record NetworkSavedConnection(string Path, string Id, string Type, string? Ssid);
