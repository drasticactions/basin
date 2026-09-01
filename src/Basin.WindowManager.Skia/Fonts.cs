using SkiaSharp;

namespace Basin.WindowManager.Skia;

public sealed class Fonts : IDisposable
{
    private readonly SKTypeface? _bundled;
    private SKTypeface? _configured;
    private bool _disposed;

    public Fonts(Stream? bundle = null, string? bundleFamily = null, string? fallbackFamily = null)
    {
        if (bundle is not null)
        {
            using var data = SKData.Create(bundle);
            _bundled = FromBundle(data, bundleFamily);
        }

        if (_bundled is null && fallbackFamily is not null)
        {
            _bundled = SKTypeface.FromFamilyName(fallbackFamily);
        }
    }

    public SKTypeface Sans => _configured ?? _bundled ?? SKTypeface.Default;

    public void SetConfigured(string? spec)
    {
        if (_configured is { } previous && !ReferenceEquals(previous, _bundled))
        {
            previous.Dispose();
        }

        _configured = null;
        if (string.IsNullOrEmpty(spec))
        {
            return;
        }

        if (spec.Contains('/') && File.Exists(spec))
        {
            _configured = SKTypeface.FromFile(spec);
            return;
        }

        var byName = SKTypeface.FromFamilyName(spec);
        if (byName is not null && byName.FamilyName == spec)
        {
            _configured = byName;
        }
        else
        {
            byName?.Dispose();
        }
    }

    public static string Ellipsize(SKFont font, string text, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        var fit = (int)font.BreakText(text, Math.Max(maxWidth - font.MeasureText("…"), 0));
        return fit <= 0 ? "…" : text[..fit] + "…";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SetConfigured(null);
        _bundled?.Dispose();
    }

    private static SKTypeface? FromBundle(SKData data, string? bundleFamily)
    {
        if (bundleFamily is not null)
        {
            for (var index = 0; ; index++)
            {
                var face = SKTypeface.FromData(data, index);
                if (face is null)
                {
                    break;
                }

                if (face.FamilyName == bundleFamily)
                {
                    return face;
                }

                face.Dispose();
            }
        }

        return SKTypeface.FromData(data, 0);
    }
}
