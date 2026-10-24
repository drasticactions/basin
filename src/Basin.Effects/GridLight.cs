namespace Basin.Effects;

/// <summary>
/// Represents one box-shaped highlight source on a canvas grid. The coordinates are in the space that the grid vertices use.
/// </summary>
/// <param name="X">The left edge of the box.</param>
/// <param name="Y">The top edge of the box.</param>
/// <param name="Width">The width of the box. <see cref="GridLights"/> stores it as zero or more.</param>
/// <param name="Height">The height of the box. <see cref="GridLights"/> stores it as zero or more.</param>
/// <param name="Strength">The weight of the source at its box, from 0 to 1.</param>
public readonly record struct GridLight(float X, float Y, float Width, float Height, float Strength)
{
    /// <summary>
    /// Gets the right edge of the box, <see cref="X"/> plus <see cref="Width"/>.
    /// </summary>
    public float Right => X + Width;

    /// <summary>
    /// Gets the bottom edge of the box, <see cref="Y"/> plus <see cref="Height"/>.
    /// </summary>
    public float Bottom => Y + Height;
}
