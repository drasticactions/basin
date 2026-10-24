namespace Basin.Capabilities.Defaults;

/// <summary>
/// Stores the hints that the tearing-control, content-type and color-representation protocols set. The writer must reset a surface's hints when the surface is destroyed.
/// </summary>
public sealed class SurfaceHints : ISurfaceHints
{
    private readonly Dictionary<Surface, bool> _tearing = [];
    private readonly Dictionary<Surface, SurfaceContentType> _types = [];
    private readonly Dictionary<Surface, (YuvCoefficients, YuvRange)> _yuv = [];

    /// <inheritdoc/>
    public (YuvCoefficients Coefficients, YuvRange Range) YuvEncodingOf(Surface surface) =>
        _yuv.TryGetValue(surface, out var encoding) ? encoding : (YuvCoefficients.Bt601, YuvRange.Limited);

    /// <summary>
    /// Sets or clears the YUV encoding of a surface.
    /// </summary>
    /// <param name="surface">The surface to change.</param>
    /// <param name="encoding">The encoding, or <see langword="null"/> to return to the default.</param>
    public void SetYuvEncoding(Surface surface, (YuvCoefficients Coefficients, YuvRange Range)? encoding)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (encoding is { } value)
        {
            _yuv[surface] = value;
        }
        else
        {
            _yuv.Remove(surface);
        }
    }

    /// <inheritdoc/>
    public bool PrefersTearing(Surface surface) => _tearing.GetValueOrDefault(surface);

    /// <inheritdoc/>
    public SurfaceContentType ContentTypeOf(Surface surface) => _types.GetValueOrDefault(surface);

    /// <summary>
    /// Sets the tearing preference of a surface.
    /// </summary>
    /// <param name="surface">The surface to change.</param>
    /// <param name="prefers"><see langword="true"/> when the surface prefers tearing, <see langword="false"/> to clear the entry.</param>
    public void SetTearing(Surface surface, bool prefers)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (prefers)
        {
            _tearing[surface] = true;
        }
        else
        {
            _tearing.Remove(surface);
        }
    }

    /// <summary>
    /// Sets the content type of a surface.
    /// </summary>
    /// <param name="surface">The surface to change.</param>
    /// <param name="type">The content type. <see cref="SurfaceContentType.None"/> clears the entry.</param>
    public void SetContentType(Surface surface, SurfaceContentType type)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (type == SurfaceContentType.None)
        {
            _types.Remove(surface);
        }
        else
        {
            _types[surface] = type;
        }
    }
}
