namespace Basin.Capabilities.Defaults;

public sealed class SurfaceHints : ISurfaceHints
{
    private readonly Dictionary<Surface, bool> _tearing = [];
    private readonly Dictionary<Surface, SurfaceContentType> _types = [];
    private readonly Dictionary<Surface, (YuvCoefficients, YuvRange)> _yuv = [];

    public (YuvCoefficients Coefficients, YuvRange Range) YuvEncodingOf(Surface surface) =>
        _yuv.TryGetValue(surface, out var encoding) ? encoding : (YuvCoefficients.Bt601, YuvRange.Limited);

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

    public bool PrefersTearing(Surface surface) => _tearing.GetValueOrDefault(surface);

    public SurfaceContentType ContentTypeOf(Surface surface) => _types.GetValueOrDefault(surface);

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
