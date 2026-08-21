namespace Basin.Capabilities;

public readonly record struct UISurfaceOptions
{
    public required UITargetKind Target { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required double Scale { get; init; }

    public DrmDeviceInfo? Device { get; init; }
}
