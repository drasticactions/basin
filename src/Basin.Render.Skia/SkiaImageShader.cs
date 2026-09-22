using SkiaSharp;

namespace Basin.Render.Skia;

public sealed class SkiaImageShader
{
    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear);

    private SKImage? _image;
    private SKShader? _shader;

    public SKShader? For(SKImage image)
    {
        if (ReferenceEquals(image, _image) && _shader is not null)
        {
            return _shader;
        }

        Drop();
        var shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
        if (shader is null)
        {
            return null;
        }

        _shader = SkiaCensus.Track(shader);
        _image = image;
        return _shader;
    }

    public void Drop()
    {
        SkiaCensus.Release(_shader);
        _shader = null;
        _image = null;
    }
}
