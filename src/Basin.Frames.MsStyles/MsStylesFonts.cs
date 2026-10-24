using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Themes.MsStyles;
using SkiaSharp;

namespace Basin.Frames.MsStyles;

/// <summary>
/// Matches the font names in a theme to installed Skia typefaces. It tries the name, the name without a weight suffix, the aliases in <see cref="MsStylesFontAliases"/>, the fallback, and then the default sans.
/// </summary>
public sealed class MsStylesFonts : IDisposable
{
    private static readonly BasinLogger Log = BasinLog.For("msstyles");

    private static readonly (string Suffix, int Weight)[] WeightSuffixes =
    [
        (" Bold", MsStylesFont.Bold),
        (" Semibold", 600),
        (" SemiBold", 600),
        (" Light", 300),
    ];

    private readonly Dictionary<(string Face, int Weight, bool Italic), SKTypeface> _resolved = [];
    private readonly HashSet<string> _logged = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SKTypeface> _owned = [];
    private readonly SKFontManager _manager;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesFonts"/> class.
    /// </summary>
    /// <param name="fallback">The consumer's typeface for a font that has no installed match. This instance does not own it.</param>
    /// <param name="manager">The font manager to search, or <see langword="null"/> for the default manager.</param>
    public MsStylesFonts(SKTypeface? fallback = null, SKFontManager? manager = null)
    {
        Fallback = fallback;
        _manager = manager ?? SKFontManager.Default;
    }

    /// <summary>
    /// Gets the consumer's typeface for a font that has no installed match.
    /// </summary>
    public SKTypeface? Fallback { get; }

    /// <summary>
    /// Gets a value indicating whether to search the installed fonts. When false, every font resolves to <see cref="Fallback"/> or the default sans.
    /// </summary>
    public bool UseSystemFonts { get; init; } = true;

    /// <summary>
    /// Gets the typeface for a theme font. The result is cached by face, weight and slant, and each substitution is logged once for each family.
    /// </summary>
    /// <param name="font">The theme font.</param>
    /// <returns>The typeface. This instance owns it, unless it is <see cref="Fallback"/> or the Skia default.</returns>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public SKTypeface Resolve(in MsStylesFont font)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = (font.Face, font.Weight, font.Italic);
        if (_resolved.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var face = font.Face;
        var weight = font.Weight;
        foreach (var (suffix, suffixWeight) in WeightSuffixes)
        {
            if (face.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && face.Length > suffix.Length)
            {
                weight = Math.Max(weight, suffixWeight);
                break;
            }
        }

        var style = new SKFontStyle((SKFontStyleWeight)weight, SKFontStyleWidth.Normal, font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var typeface = UseSystemFonts ? Match(face, style) : null;
        if (typeface is null && UseSystemFonts)
        {
            var stem = face;
            foreach (var (suffix, _) in WeightSuffixes)
            {
                if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && stem.Length > suffix.Length)
                {
                    stem = stem[..^suffix.Length];
                    break;
                }
            }

            typeface = stem != face ? Match(stem, style) : null;
            if (typeface is null)
            {
                foreach (var alias in MsStylesFontAliases.For(face).Concat(stem != face ? MsStylesFontAliases.For(stem) : []))
                {
                    typeface = Match(alias, style);
                    if (typeface is not null)
                    {
                        Once(face, $"{face} is not installed; drawing its titles in {alias}");
                        break;
                    }
                }
            }
        }

        if (typeface is null && Fallback is not null)
        {
            typeface = Fallback;
            if (UseSystemFonts)
            {
                Once(face, $"{face} is not installed and has no installed alias; using the consumer font {Fallback.FamilyName}");
            }
        }

        if (typeface is null)
        {
            typeface = Track(_manager.MatchFamily("sans-serif", style)) ?? SKTypeface.Default;
            Once(face, $"{face} is not installed; using the default sans {typeface.FamilyName}");
        }

        _resolved[key] = typeface;
        return typeface;
    }

    private SKTypeface? Match(string family, SKFontStyle style)
    {
        var typeface = _manager.MatchFamily(family, style);
        if (typeface is null)
        {
            return null;
        }

        if (!string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
        {
            typeface.Dispose();
            return null;
        }

        return Track(typeface);
    }

    private SKTypeface? Track(SKTypeface? typeface)
    {
        if (typeface is not null)
        {
            _owned.Add(SkiaCensus.Track(typeface));
        }

        return typeface;
    }

    private void Once(string face, string message)
    {
        if (_logged.Add(face))
        {
            Log.Info($"{message}");
        }
    }

    /// <summary>
    /// Releases the typefaces that the matcher found. It does not release <see cref="Fallback"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var typeface in _owned)
        {
            SkiaCensus.Release(typeface);
        }

        _owned.Clear();
        _resolved.Clear();
    }
}
