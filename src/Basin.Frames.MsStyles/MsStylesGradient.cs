using Basin.Themes.MsStyles;

namespace Basin.Frames.MsStyles;

/// <summary>
/// A theme gradient of up to five colors with their ratios. It is a value type, so a painter can use it as a cache key.
/// </summary>
public struct MsStylesGradient : IEquatable<MsStylesGradient>
{
    /// <summary>
    /// The fill type. A horizontal or radial gradient draws as named, a solid fill uses <see cref="Color0"/>, and any other type draws a vertical gradient.
    /// </summary>
    public FillType Kind;
    /// <summary>
    /// The number of colors in use, from 0 to 5. A gradient with no colors draws nothing.
    /// </summary>
    public int Count;
    /// <summary>
    /// The first color as 0xAARRGGBB.
    /// </summary>
    public uint Color0;
    /// <summary>
    /// The second color as 0xAARRGGBB.
    /// </summary>
    public uint Color1;
    /// <summary>
    /// The third color as 0xAARRGGBB.
    /// </summary>
    public uint Color2;
    /// <summary>
    /// The fourth color as 0xAARRGGBB.
    /// </summary>
    public uint Color3;
    /// <summary>
    /// The fifth color as 0xAARRGGBB.
    /// </summary>
    public uint Color4;
    /// <summary>
    /// The position of the first color, from 0 to 255. If any ratio in use is negative, the painter spaces the colors evenly.
    /// </summary>
    public int Ratio0;
    /// <summary>
    /// The position of the second color, from 0 to 255.
    /// </summary>
    public int Ratio1;
    /// <summary>
    /// The position of the third color, from 0 to 255.
    /// </summary>
    public int Ratio2;
    /// <summary>
    /// The position of the fourth color, from 0 to 255.
    /// </summary>
    public int Ratio3;
    /// <summary>
    /// The position of the fifth color, from 0 to 255.
    /// </summary>
    public int Ratio4;

    /// <summary>
    /// Gets a color by its index.
    /// </summary>
    /// <param name="index">The index from 0 to 4. An index above 4 reads <see cref="Color4"/>.</param>
    /// <returns>The color as 0xAARRGGBB.</returns>
    public readonly uint ColorAt(int index) => index switch
    {
        0 => Color0,
        1 => Color1,
        2 => Color2,
        3 => Color3,
        _ => Color4,
    };

    /// <summary>
    /// Gets a ratio by its index.
    /// </summary>
    /// <param name="index">The index from 0 to 4. An index above 4 reads <see cref="Ratio4"/>.</param>
    /// <returns>The position from 0 to 255.</returns>
    public readonly int RatioAt(int index) => index switch
    {
        0 => Ratio0,
        1 => Ratio1,
        2 => Ratio2,
        3 => Ratio3,
        _ => Ratio4,
    };

    /// <summary>
    /// Sets one color and its ratio. It does not change <see cref="Count"/>.
    /// </summary>
    /// <param name="index">The index from 0 to 4. An index above 4 sets the fifth entry.</param>
    /// <param name="color">The color as 0xAARRGGBB.</param>
    /// <param name="ratio">The position from 0 to 255.</param>
    public void Set(int index, uint color, int ratio)
    {
        switch (index)
        {
            case 0: Color0 = color; Ratio0 = ratio; break;
            case 1: Color1 = color; Ratio1 = ratio; break;
            case 2: Color2 = color; Ratio2 = ratio; break;
            case 3: Color3 = color; Ratio3 = ratio; break;
            default: Color4 = color; Ratio4 = ratio; break;
        }
    }

    /// <summary>
    /// Compares every field with another gradient, including the colors and ratios beyond <see cref="Count"/>.
    /// </summary>
    /// <param name="other">The gradient to compare with.</param>
    /// <returns><see langword="true"/> when all fields are equal.</returns>
    public readonly bool Equals(MsStylesGradient other) =>
        Kind == other.Kind && Count == other.Count &&
        Color0 == other.Color0 && Color1 == other.Color1 && Color2 == other.Color2 && Color3 == other.Color3 && Color4 == other.Color4 &&
        Ratio0 == other.Ratio0 && Ratio1 == other.Ratio1 && Ratio2 == other.Ratio2 && Ratio3 == other.Ratio3 && Ratio4 == other.Ratio4;

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is MsStylesGradient other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode() =>
        HashCode.Combine(Kind, Count, Color0, Color1, Color2, Color3, Color4, HashCode.Combine(Ratio0, Ratio1, Ratio2, Ratio3, Ratio4));

    /// <summary>
    /// Compares two gradients field by field.
    /// </summary>
    /// <param name="left">The first gradient.</param>
    /// <param name="right">The second gradient.</param>
    /// <returns><see langword="true"/> when all fields are equal.</returns>
    public static bool operator ==(MsStylesGradient left, MsStylesGradient right) => left.Equals(right);

    /// <summary>
    /// Compares two gradients field by field.
    /// </summary>
    /// <param name="left">The first gradient.</param>
    /// <param name="right">The second gradient.</param>
    /// <returns><see langword="true"/> when a field differs.</returns>
    public static bool operator !=(MsStylesGradient left, MsStylesGradient right) => !left.Equals(right);
}
