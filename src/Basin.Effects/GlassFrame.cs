using System.Numerics;
using Basin.Capabilities;

namespace Basin.Effects;

internal readonly record struct GlassFrame
{
    public Vector4 Box { get; init; }

    public Vector4 Clamp { get; init; }

    public Vector4 Hole { get; init; }

    public Vector4 Highlight { get; init; }

    public Vector4 Shape { get; init; }

    public Vector4 Light { get; init; }

    public Vector4 Tint { get; init; }

    public Vector4 Extra { get; init; }

    public static GlassFrame For(
        in GlassOptions options, in FrameBackdropShape shape, in Box extent, in Box bounds, bool linear)
    {
        var known = shape.Width > 0 && shape.Height > 0;
        return Build(
            options, shape, extent.X, extent.Y, extent.Width, extent.Height,
            known ? extent.Width / shape.Width : 1.0, known ? extent.Height / shape.Height : 1.0, bounds, linear);
    }

    public static GlassFrame ForLocal(in GlassOptions options, in FrameBackdropShape shape, in Box bounds, bool linear) =>
        Build(options, shape, 0, 0, shape.Width, shape.Height, 1.0, 1.0, bounds, linear);

    private static GlassFrame Build(
        in GlassOptions options, in FrameBackdropShape shape, double x, double y, double width, double height,
        double sx, double sy, in Box bounds, bool linear)
    {
        var known = shape.Width > 0 && shape.Height > 0;
        var emphasis = known ? Math.Clamp(shape.Emphasis, 0, 1) : 1.0;
        var hole = known ? shape.ClientHole : default;
        var (lx, ly) = GlassField.TowardLight(options.LightAngle);
        return new GlassFrame
        {
            Box = new Vector4((float)x, (float)y, (float)width, (float)height),
            Clamp = new Vector4(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Right - 0.5f, bounds.Bottom - 0.5f),
            Hole = hole.IsEmpty
                ? Vector4.Zero
                : new Vector4(
                    (float)(x + (hole.X * sx)),
                    (float)(y + (hole.Y * sy)),
                    (float)(hole.Width * sx),
                    (float)(hole.Height * sy)),
            Highlight = known
                ? new Vector4(
                    (float)(x + (shape.HighlightX * sx)),
                    (float)(y + (shape.HighlightY * sy)),
                    (float)Math.Max(0, shape.HighlightStrength * options.Glint),
                    (float)(options.GlintRadius * sx))
                : Vector4.Zero,
            Shape = new Vector4(
                (float)(known ? Math.Max(0, shape.CornerRadius) * sx : 0),
                (float)(known ? Math.Max(0, shape.EdgeWidth) * sx : 0),
                (float)Math.Max(0, options.Lens),
                (float)(Math.Max(0, options.Specular) * (0.5 + (0.5 * emphasis)))),
            Light = new Vector4(
                (float)lx,
                (float)ly,
                hole.IsEmpty ? 0f : (float)(Math.Max(0, options.ContentBorderWidth) * sx),
                hole.IsEmpty ? 0f : (float)(Math.Max(0, shape.ClientRadius) * sx)),
            Tint = Packed(
                Mix(options.InactiveTint, options.Tint, emphasis, linear),
                Mix(options.InactiveContentBorder, options.ContentBorder, emphasis, linear)),
            Extra = new Vector4((float)options.Saturation, (float)Math.Max(0, options.Noise), 1f, 0f),
        };
    }

    public static int Factor(int strength, out double sigma)
    {
        sigma = BlurStrength.For(strength).Radius / 3;
        var factor = 1;
        while (sigma / factor > 3 && factor < 64)
        {
            factor *= 2;
        }

        sigma /= factor;
        return factor;
    }

    private static Vector4 Packed(Vector4 tint, Vector4 border) => new(
        Rgb24(tint),
        Rgb24(border),
        tint.W,
        border.W);

    private static float Rgb24(Vector4 color) =>
        (MathF.Round(Math.Clamp(color.X, 0f, 1f) * 255f) * 65536f)
        + (MathF.Round(Math.Clamp(color.Y, 0f, 1f) * 255f) * 256f)
        + MathF.Round(Math.Clamp(color.Z, 0f, 1f) * 255f);

    private static Vector4 Mix(uint from, uint to, double t, bool linear)
    {
        var a = Channel(from, 24, false) + ((Channel(to, 24, false) - Channel(from, 24, false)) * t);
        var r = Channel(from, 16, linear) + ((Channel(to, 16, linear) - Channel(from, 16, linear)) * t);
        var g = Channel(from, 8, linear) + ((Channel(to, 8, linear) - Channel(from, 8, linear)) * t);
        var b = Channel(from, 0, linear) + ((Channel(to, 0, linear) - Channel(from, 0, linear)) * t);
        return new Vector4((float)r, (float)g, (float)b, (float)a);
    }

    private static double Channel(uint argb, int shift, bool linear)
    {
        var value = ((argb >> shift) & 0xFF) / 255.0;
        if (!linear)
        {
            return value;
        }

        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
