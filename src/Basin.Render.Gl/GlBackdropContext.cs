namespace Basin.Render.Gl;

/// <summary>
/// Holds what a <see cref="IGlBackdropEffect"/> reads when it draws one region. The context of the device is current during the call.
/// </summary>
public readonly record struct GlBackdropContext
{
    /// <summary>
    /// Gets the device whose context is current.
    /// </summary>
    public required GlDevice Device { get; init; }

    /// <summary>
    /// Gets the name of a 2D texture that holds the pixels already drawn. For a CPU target it is a copy that the renderer made for this call.
    /// </summary>
    public required uint Backdrop { get; init; }

    /// <summary>
    /// Gets the width of the target in pixels.
    /// </summary>
    public required int TargetWidth { get; init; }

    /// <summary>
    /// Gets the height of the target in pixels.
    /// </summary>
    public required int TargetHeight { get; init; }

    /// <summary>
    /// Gets the region of the effect in target pixels, clipped to the target.
    /// </summary>
    public required Box Bounds { get; init; }

    /// <summary>
    /// Gets the whole box of the node in target pixels, before the clip. It is empty when the caller did not give one.
    /// </summary>
    public Box Extent { get; init; }

    /// <summary>
    /// Gets the triangles of a deformed node, or an empty span for a node that no mesh deforms. It is valid only during the call.
    /// </summary>
    public ReadOnlyMemory<MeshVertex> Mesh { get; init; }

    /// <summary>
    /// Gets the key under which the effect keeps state for this node, or <see langword="null"/>.
    /// </summary>
    public object? Key { get; init; }
}
