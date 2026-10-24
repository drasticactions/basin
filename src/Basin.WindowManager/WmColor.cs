namespace Basin.WindowManager;

/// <summary>
/// A color with four 32-bit channels, each a fraction of 0xffffffff, with the color channels premultiplied by alpha.
/// </summary>
public readonly record struct WmColor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WmColor"/> struct. The channels are used as given, with no premultiplication.
    /// </summary>
    /// <param name="r">The red channel, already premultiplied.</param>
    /// <param name="g">The green channel, already premultiplied.</param>
    /// <param name="b">The blue channel, already premultiplied.</param>
    /// <param name="a">The alpha channel.</param>
    public WmColor(uint r, uint g, uint b, uint a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>
    /// Gets the premultiplied red channel as a fraction of 0xffffffff.
    /// </summary>
    public uint R { get; }

    /// <summary>
    /// Gets the premultiplied green channel as a fraction of 0xffffffff.
    /// </summary>
    public uint G { get; }

    /// <summary>
    /// Gets the premultiplied blue channel as a fraction of 0xffffffff.
    /// </summary>
    public uint B { get; }

    /// <summary>
    /// Gets the alpha channel as a fraction of 0xffffffff.
    /// </summary>
    public uint A { get; }

    /// <summary>
    /// Gets the color with all four channels zero.
    /// </summary>
    public static WmColor Transparent => default;

    /// <summary>
    /// Converts straight 8-bit channels to a premultiplied 32-bit color. Each byte scales by 0x01010101, so 0xff maps to 0xffffffff.
    /// </summary>
    /// <param name="r">The red channel, not premultiplied.</param>
    /// <param name="g">The green channel, not premultiplied.</param>
    /// <param name="b">The blue channel, not premultiplied.</param>
    /// <param name="a">The alpha channel. The default is opaque.</param>
    /// <returns>The color with red, green and blue premultiplied by alpha.</returns>
    public static WmColor FromRgba(byte r, byte g, byte b, byte a = 0xff)
    {
        const uint Replicate = 0x01010101u;
        var alpha = a * Replicate;
        return new WmColor(
            Premultiply(r * Replicate, a),
            Premultiply(g * Replicate, a),
            Premultiply(b * Replicate, a),
            alpha);
    }

    /// <summary>
    /// Converts a packed 0xRRGGBBAA value with straight alpha to a premultiplied 32-bit color.
    /// </summary>
    /// <param name="rgba">The color with red in the highest byte and alpha in the lowest byte.</param>
    /// <returns>The color with red, green and blue premultiplied by alpha.</returns>
    public static WmColor FromRgba(uint rgba) => FromRgba(
        (byte)(rgba >> 24),
        (byte)(rgba >> 16),
        (byte)(rgba >> 8),
        (byte)rgba);

    private static uint Premultiply(uint channel, byte alpha) =>
        alpha == 0xff ? channel : (uint)(channel * (double)alpha / 0xff);
}
