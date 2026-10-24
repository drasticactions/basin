namespace Basin.Render.Gl;

/// <summary>
/// Holds what a <see cref="IGlFilter"/> reads when it draws one frame. The context of the device is current during the call.
/// </summary>
public readonly struct GlFilterContext
{
    /// <summary>
    /// Gets the device whose context is current.
    /// </summary>
    public required GlDevice Device { get; init; }

    /// <summary>
    /// Gets the name of the 2D texture that holds the composed frame.
    /// </summary>
    public required uint Source { get; init; }

    /// <summary>
    /// Gets the width of <see cref="Source"/> in pixels.
    /// </summary>
    public required int SourceWidth { get; init; }

    /// <summary>
    /// Gets the height of <see cref="Source"/> in pixels.
    /// </summary>
    public required int SourceHeight { get; init; }

    /// <summary>
    /// Gets the name of the texture that backs the target framebuffer.
    /// </summary>
    public required uint Target { get; init; }

    /// <summary>
    /// Gets the width of the target in pixels.
    /// </summary>
    public required int TargetWidth { get; init; }

    /// <summary>
    /// Gets the height of the target in pixels.
    /// </summary>
    public required int TargetHeight { get; init; }

    /// <summary>
    /// Gets the region of the target to write, in target pixels.
    /// </summary>
    public required Box Viewport { get; init; }

    /// <summary>
    /// Gets the per-frame values for the filter.
    /// </summary>
    public required FrameFilterOptions Options { get; init; }
}
