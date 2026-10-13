namespace Basin.Capabilities;

public interface ISurfaceHints
{
    bool PrefersTearing(Surface surface);

    SurfaceContentType ContentTypeOf(Surface surface);

    (YuvCoefficients Coefficients, YuvRange Range) YuvEncodingOf(Surface surface) => (YuvCoefficients.Bt601, YuvRange.Limited);
}
