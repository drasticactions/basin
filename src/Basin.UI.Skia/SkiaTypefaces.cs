using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Basin.UI.Skia;

public static class SkiaTypefaces
{
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
