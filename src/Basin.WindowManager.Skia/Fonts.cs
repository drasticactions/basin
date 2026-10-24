using SkiaSharp;

namespace Basin.WindowManager.Skia;

/// <summary>
/// The sans typeface of a manager: a configured face, else a face from a bundled font, else a fallback family, else Skia's default.
/// </summary>
public sealed class Fonts : IDisposable
{
    private readonly SKTypeface? _bundled;
    private SKTypeface? _configured;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Fonts"/> class. It reads the whole bundle now.
    /// </summary>
    /// <param name="bundle">A font file or collection that the program carries, or <see langword="null"/> for none. The caller keeps ownership of the stream.</param>
    /// <param name="bundleFamily">The family to pick from a collection, or <see langword="null"/> for the first face.</param>
    /// <param name="fallbackFamily">The system family to use when there is no bundled face.</param>
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

    /// <summary>
    /// Gets the typeface to draw with. It is never <see langword="null"/>. The returned face stays owned by this object.
    /// </summary>
    public SKTypeface Sans => _configured ?? _bundled ?? SKTypeface.Default;

    /// <summary>
    /// Sets the configured face from a user setting, and disposes the previous configured face.
    /// </summary>
    /// <param name="spec">A font file path that contains '/', or a family name that must match exactly. <see langword="null"/>, empty or an unmatched value clears the configured face.</param>
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

    /// <summary>
    /// Shortens text with a trailing ellipsis so it fits a width.
    /// </summary>
    /// <param name="font">The font that measures the text.</param>
    /// <param name="text">The text to fit.</param>
    /// <param name="maxWidth">The available width in the font's units.</param>
    /// <returns>The text unchanged when it fits, else the longest prefix that fits with "…" after it, or "…" alone.</returns>
    public static string Ellipsize(SKFont font, string text, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        var fit = (int)font.BreakText(text, Math.Max(maxWidth - font.MeasureText("…"), 0));
        return fit <= 0 ? "…" : text[..fit] + "…";
    }

    /// <summary>
    /// Disposes the configured and bundled faces. A second call does nothing.
    /// </summary>
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
