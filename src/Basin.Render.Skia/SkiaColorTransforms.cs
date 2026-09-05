using Basin.Capabilities;
using Basin.Color;
using Basin.Diagnostics;

namespace Basin.Render.Skia;

internal sealed class SkiaColorTransforms : IDisposable
{
    private readonly Dictionary<(ImageDescription Source, ImageDescription Output), SkiaColorTransform?> _transforms =
        new(ImageDescriptionPairComparer.Instance);

    internal SkiaColorTransform? TransformFor(ImageDescription? source, ImageDescription? output)
    {
        source ??= ImageDescription.SdrDefault;
        output ??= ImageDescription.SdrDefault;
        if (ReferenceEquals(source, output))
        {
            return null;
        }

        var key = (source, output);
        if (_transforms.TryGetValue(key, out var cached))
        {
            return cached;
        }

        AllocationScope.Pause();
        try
        {
            var transform = source.IccData is null && !ColorLutBaker.IsIdentity(source, output)
                ? SkiaColorTransform.Create(ColorTransformParameters.From(source, output))
                : null;
            _transforms[key] = transform;
            return transform;
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    internal RenderColor ConvertRect(in RenderColor color, ImageDescription? output)
    {
        if (output is null || color.A <= 0 || TransformFor(null, output) is not { } transform)
        {
            return color;
        }

        Span<double> rgb = stackalloc double[3];
        rgb[0] = Math.Clamp(color.R / color.A, 0, 1);
        rgb[1] = Math.Clamp(color.G / color.A, 0, 1);
        rgb[2] = Math.Clamp(color.B / color.A, 0, 1);
        transform.Parameters.Apply(rgb);
        return new RenderColor((float)rgb[0] * color.A, (float)rgb[1] * color.A, (float)rgb[2] * color.A, color.A);
    }

    public void Dispose()
    {
        foreach (var transform in _transforms.Values)
        {
            transform?.Dispose();
        }

        _transforms.Clear();
    }
}
