namespace Basin.Capabilities;

public readonly record struct InputDeviceInfo(
    ulong Id,
    string Name,
    InputDeviceCapability Capabilities,
    string? OutputName);
