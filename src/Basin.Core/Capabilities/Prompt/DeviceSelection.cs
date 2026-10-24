namespace Basin.Capabilities;

/// <summary>
/// Holds the answer the user gave to a <see cref="DevicePrompt"/>.
/// </summary>
/// <param name="Devices">The device types that the user granted. It can be fewer than the request.</param>
/// <param name="Clipboard">A value indicating whether the user granted clipboard access.</param>
/// <param name="PersistMode">The portal persist mode the user chose: 0 for none, 1 for this session, 2 until revoked. The caller clamps it to the requested mode.</param>
public readonly record struct DeviceSelection(InputDeviceCapability Devices, bool Clipboard, uint PersistMode);
