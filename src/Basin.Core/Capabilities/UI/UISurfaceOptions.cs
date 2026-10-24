namespace Basin.Capabilities;

/// <summary>
/// The parameters for <see cref="IUIHost.CreateSurface"/>.
/// </summary>
public readonly record struct UISurfaceOptions
{
    /// <summary>
    /// Gets the buffer kind to produce. It must be one of the kinds in <see cref="IUIHost.Produces"/>.
    /// </summary>
    public required UITargetKind Target { get; init; }

    /// <summary>
    /// Gets the width in logical pixels.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the height in logical pixels.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets the scale from logical to buffer pixels.
    /// </summary>
    public required double Scale { get; init; }

    /// <summary>
    /// Gets the DRM device that imports the buffers, or <see langword="null"/> when it is unknown.
    /// </summary>
    public DrmDeviceInfo? Device { get; init; }
}
