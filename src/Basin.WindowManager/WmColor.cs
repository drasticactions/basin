namespace Basin.WindowManager;

public readonly record struct WmColor
{
    public WmColor(uint r, uint g, uint b, uint a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public uint R { get; }

    public uint G { get; }

    public uint B { get; }

    public uint A { get; }

    public static WmColor Transparent => default;

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

    public static WmColor FromRgba(uint rgba) => FromRgba(
        (byte)(rgba >> 24),
        (byte)(rgba >> 16),
        (byte)(rgba >> 8),
        (byte)rgba);

    private static uint Premultiply(uint channel, byte alpha) =>
        alpha == 0xff ? channel : (uint)(channel * (double)alpha / 0xff);
}
