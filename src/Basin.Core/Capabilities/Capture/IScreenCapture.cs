namespace Basin.Capabilities;

/// <summary>
/// Copies outputs, windows, layout regions and the cursor into buffers. One implementation serves every capture protocol and the screencast portal.
/// </summary>
public interface IScreenCapture
{
    /// <summary>
    /// Gets a value that tells whether the implementation can capture a source now.
    /// </summary>
    /// <param name="source">The source to capture.</param>
    /// <returns><see langword="true"/> when the source can be captured, otherwise <see langword="false"/>.</returns>
    bool Supports(in CaptureSource source);

    /// <summary>
    /// Gets the size and the preferred pixel format of a capture of a source.
    /// </summary>
    /// <param name="source">The source to capture.</param>
    /// <param name="format">The size in buffer pixels and the format, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the source exists and has a size, otherwise <see langword="false"/>.</returns>
    bool TryDescribe(in CaptureSource source, out CaptureFormat format);

    /// <summary>
    /// Draws a source, or part of it, into a buffer. It must not allocate on the frame path.
    /// </summary>
    /// <param name="source">The source to capture.</param>
    /// <param name="region">The area to copy in source buffer pixels, or an empty box for the whole source.</param>
    /// <param name="target">The buffer that receives the pixels. Its size must match the region.</param>
    /// <returns><see langword="true"/> when the buffer holds the new pixels, otherwise <see langword="false"/>.</returns>
    bool Capture(in CaptureSource source, in Box region, IBuffer target);

    /// <summary>
    /// Gets a value indicating whether <see cref="Capture"/> can write into an NV12 buffer. The default is <see langword="false"/>.
    /// </summary>
    bool CanCaptureYuv => false;

    /// <summary>
    /// Gets a value indicating whether <see cref="Capture"/> can write into a half-float buffer. The default is <see langword="false"/>.
    /// </summary>
    bool CanCaptureHalfFloat => false;

    /// <summary>
    /// Gets the name of the renderer that draws the captures, for diagnostics. It is <see langword="null"/> when there is none or it is not known.
    /// </summary>
    string? RendererName => null;

    /// <summary>
    /// Gets the color space of the pixels that a capture of a source produces.
    /// </summary>
    /// <param name="source">The source to capture.</param>
    /// <param name="color">The color description and HDR metadata, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the color space is known. The default returns <see langword="false"/>.</returns>
    bool TryColorOf(in CaptureSource source, out CaptureColor color)
    {
        color = default;
        return false;
    }

    /// <summary>
    /// Gets the cursor that shows on an output, for cursor metadata in a stream.
    /// </summary>
    /// <param name="output">The output to look at.</param>
    /// <param name="cursor">The cursor, with its position in output buffer pixels, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a visible cursor overlaps the output, otherwise <see langword="false"/>.</returns>
    bool TryCursorState(IOutput output, out CaptureCursorState cursor);

    /// <summary>
    /// Sets the cursor image and its position that captures report and overlay. A change notifies the damage observers through <see cref="ICaptureDamageObserver.OnCursorChanged"/>.
    /// </summary>
    /// <param name="image">The cursor image, or <see langword="null"/> when no cursor shows.</param>
    /// <param name="state">The hotspot position in layout coordinates and the image geometry in buffer pixels.</param>
    void SetCursor(IBuffer? image, in CaptureCursorState state);

    /// <summary>
    /// Adds an observer that is told when a capture source changes.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddDamageObserver(ICaptureDamageObserver observer);

    /// <summary>
    /// Removes an observer.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveDamageObserver(ICaptureDamageObserver observer);
}
