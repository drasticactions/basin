namespace Basin.Capabilities;

/// <summary>
/// Gives the presentation hints that clients set for their surfaces: tearing, content type and YUV encoding.
/// </summary>
public interface ISurfaceHints
{
    /// <summary>
    /// Gets a value that tells whether a surface asked for tearing (asynchronous) page flips.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns><see langword="true"/> when the client prefers tearing, otherwise <see langword="false"/>.</returns>
    bool PrefersTearing(Surface surface);

    /// <summary>
    /// Gets the content type that a client declared for a surface.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The declared type, or <see cref="SurfaceContentType.None"/> when the client declared none.</returns>
    SurfaceContentType ContentTypeOf(Surface surface);

    /// <summary>
    /// Gets the YUV matrix and range that a client declared for a surface with YUV buffers.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The declared encoding. The default is BT.601 with limited range.</returns>
    (YuvCoefficients Coefficients, YuvRange Range) YuvEncodingOf(Surface surface) => (YuvCoefficients.Bt601, YuvRange.Limited);
}
