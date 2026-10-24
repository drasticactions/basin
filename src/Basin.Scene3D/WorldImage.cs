namespace Basin.Scene3D;

/// <summary>
/// An RGBA image in memory, for a mesh texture. A pass uploads it one time and caches it by reference.
/// </summary>
public sealed class WorldImage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorldImage"/> class. The image keeps <paramref name="rgba"/> and does not copy it.
    /// </summary>
    /// <param name="width">The width in pixels. It must be positive.</param>
    /// <param name="height">The height in pixels. It must be positive.</param>
    /// <param name="rgba">The pixels, four bytes each, top row first. The length must be the width times the height times 4.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException">The length of <paramref name="rgba"/> does not match the size.</exception>
    public WorldImage(int width, int height, byte[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        ArgumentNullException.ThrowIfNull(rgba);
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("RGBA data does not match the size.", nameof(rgba));
        }

        Width = width;
        Height = height;
        Rgba = rgba;
    }

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the RGBA pixels, four bytes each, top row first.
    /// </summary>
    public byte[] Rgba { get; }
}
