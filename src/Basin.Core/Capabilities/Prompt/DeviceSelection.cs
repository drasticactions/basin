namespace Basin.Capabilities;

public readonly record struct DeviceSelection(InputDeviceCapability Devices, bool Clipboard, uint PersistMode);
