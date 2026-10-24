using Basin.Diagnostics;
using SkiaSharp;

namespace Basin.Render.Skia;

/// <summary>
/// Caches one clamped, linear-sampled <see cref="SKShader"/> for the last image it was asked about.
/// It owns the shader but not the image.
/// </summary>
public sealed class SkiaImageShader
{
    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear);

    private SKImage? _image;
    private SKShader? _shader;

    /// <summary>
    /// Gets the shader for an image. It makes a new shader only when the image differs from the last
    /// one, and drops the old shader then.
    /// </summary>
    /// <param name="image">The image to sample.</param>
    /// <returns>The cached shader, which stays valid until the next call with another image or <see cref="Drop"/>. It is <see langword="null"/> when Skia refuses the image.</returns>
    public SKShader? For(SKImage image)
    {
        if (ReferenceEquals(image, _image) && _shader is not null)
        {
            return _shader;
        }

        Drop();
        AllocationScope.Pause();
        var shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
        AllocationScope.Resume();
        if (shader is null)
        {
            return null;
        }

        _shader = SkiaCensus.Track(shader);
        _image = image;
        return _shader;
    }

    /// <summary>
    /// Releases the cached shader. The next <see cref="For"/> call makes a new one.
    /// </summary>
    public void Drop()
    {
        SkiaCensus.Release(_shader);
        _shader = null;
        _image = null;
    }
}
