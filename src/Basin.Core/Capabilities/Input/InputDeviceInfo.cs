namespace Basin.Capabilities;

/// <summary>Describes one input device.</summary>
/// <param name="Id">The identifier of the device. It stays the same while the device is connected.</param>
/// <param name="Name">The name that the device reports.</param>
/// <param name="Capabilities">The kinds of input that the device produces.</param>
/// <param name="Type">The main type of the device.</param>
/// <param name="OutputName">The output that the device is mapped to, or <see langword="null"/> when it is not mapped.</param>
/// <param name="Sysname">The kernel name of the device node, for example <c>event5</c>, or <see langword="null"/> when it is unknown.</param>
/// <param name="Vendor">The USB or bus vendor identifier, or 0 when it is unknown.</param>
/// <param name="Product">The product identifier, or 0 when it is unknown.</param>
/// <param name="Bus">The bus type, or 0 when it is unknown.</param>
public readonly record struct InputDeviceInfo(
    ulong Id,
    string Name,
    InputDeviceCapability Capabilities,
    InputDeviceType Type,
    string? OutputName,
    string? Sysname = null,
    uint Vendor = 0,
    uint Product = 0,
    uint Bus = 0);
