namespace Basin.Capabilities;

/// <summary>
/// Describes one tablet that an <see cref="ITabletSource"/> reports.
/// </summary>
/// <param name="Id">The id of the tablet. It is unique within the source.</param>
/// <param name="Name">The device name.</param>
/// <param name="VendorId">The USB vendor id, or 0 when it is not known.</param>
/// <param name="ProductId">The USB product id, or 0 when it is not known.</param>
/// <param name="Path">The device node path, for example <c>/dev/input/event5</c>. It can be empty.</param>
/// <param name="BusType">The Linux bus type of the device, for example 3 for USB, or 0 when it is not known.</param>
public readonly record struct TabletInfo(
    ulong Id,
    string Name,
    uint VendorId,
    uint ProductId,
    string Path,
    uint BusType = 0);
