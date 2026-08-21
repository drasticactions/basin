namespace Basin.Capabilities;

public readonly record struct TabletInfo(
    ulong Id,
    string Name,
    uint VendorId,
    uint ProductId,
    string Path,
    uint BusType = 0);
