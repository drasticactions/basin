namespace Basin.Ipc;

/// <summary>
/// One input device, as <c>devices/list</c> and the <c>devices/added</c> event write it.
/// </summary>
/// <param name="Id">The device id that the <c>devices</c> methods take.</param>
/// <param name="Name">The device name that the kernel reports.</param>
/// <param name="Type">The device type: <c>keyboard</c>, <c>pointer</c>, <c>touchpad</c>, <c>touch</c>, <c>tablet_tool</c>, <c>tablet_pad</c> or <c>switch</c>.</param>
/// <param name="Capabilities">The capability words: <c>keyboard</c>, <c>pointer</c>, <c>touch</c>, <c>gesture</c>, <c>switch</c>, <c>tablet_tool</c> and <c>tablet_pad</c>.</param>
/// <param name="Sysname">The kernel name of the device node, for example <c>event5</c>, or <see langword="null"/> when it is unknown.</param>
/// <param name="Vendor">The vendor id, or 0 when it is unknown.</param>
/// <param name="Product">The product id, or 0 when it is unknown.</param>
/// <param name="Bus">The bus type, or 0 when it is unknown.</param>
/// <param name="Output">The name of the output that libinput maps the device to, or <see langword="null"/> when it is not mapped.</param>
/// <param name="Rules">The indexes of the config rules that the device matched. It is empty without a rule source.</param>
public readonly record struct IpcDevice(
    ulong Id,
    string Name,
    string Type,
    ReadOnlyMemory<string> Capabilities,
    string? Sysname,
    uint Vendor,
    uint Product,
    uint Bus,
    string? Output,
    ReadOnlyMemory<int> Rules);
