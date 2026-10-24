using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Basin.UI.Skia;

/// <summary>
/// Loads Skia typefaces.
/// </summary>
public static class SkiaTypefaces
{
    /// <summary>
    /// Finds a face by family name in a font collection, for example a .ttc file. The search looks at the first 64 faces.
    /// </summary>
    /// <param name="data">The font file data.</param>
    /// <param name="familyName">The family name to match exactly.</param>
    /// <returns>The typeface, counted in <see cref="SkiaCensus"/>, which the caller must release. <see langword="null"/> when no face matches.</returns>
    public static SKTypeface? FromCollection(SKData data, string familyName)
    {
        for (var index = 0; index < 64; index++)
        {
            var face = SKTypeface.FromData(data, index);
            if (face is null)
            {
                return null;
            }

            if (face.FamilyName == familyName)
            {
                return SkiaCensus.Track(face);
            }

            face.Dispose();
        }

        return null;
    }
}
