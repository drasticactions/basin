namespace Basin.SystemStatus;

/// <summary>
/// One BlueZ device on the adapter that <see cref="BluetoothStatus"/> reads. The reader lists only paired or connected devices.
/// </summary>
/// <param name="Path">The D-Bus object path of the <c>org.bluez.Device1</c> object.</param>
/// <param name="Name">The display name: the alias, else the name, else the address.</param>
/// <param name="Address">The Bluetooth address, or an empty string when BlueZ gives none.</param>
/// <param name="Icon">The icon name that BlueZ gives, or <see langword="null"/>.</param>
/// <param name="Paired">A value indicating whether the device is paired.</param>
/// <param name="Connected">A value indicating whether the device is connected now.</param>
public sealed record BluetoothDevice(string Path, string Name, string Address, string? Icon, bool Paired, bool Connected);
