namespace Basin.Themes.MsStyles;

/// <summary>
/// Holds a rectangle value of a theme property as four edges, in pixels.
/// </summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Bottom">The bottom edge.</param>
public readonly record struct MsStylesRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>
    /// Gets the width, <see cref="Right"/> minus <see cref="Left"/>. It is negative when the edges are reversed.
    /// </summary>
    public int Width => Right - Left;

    /// <summary>
    /// Gets the height, <see cref="Bottom"/> minus <see cref="Top"/>. It is negative when the edges are reversed.
    /// </summary>
    public int Height => Bottom - Top;
}
