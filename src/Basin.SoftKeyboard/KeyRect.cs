namespace Basin.SoftKeyboard;

/// <summary>
/// A rectangle in keyboard coordinates, in logical pixels.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct KeyRect(float X, float Y, float Width, float Height)
{
    /// <summary>
    /// Gets the right edge, which <see cref="Contains"/> excludes.
    /// </summary>
    public float Right => X + Width;

    /// <summary>
    /// Gets the bottom edge, which <see cref="Contains"/> excludes.
    /// </summary>
    public float Bottom => Y + Height;

    /// <summary>
    /// Gets the horizontal center.
    /// </summary>
    public float CenterX => X + Width / 2;

    /// <summary>
    /// Gets the vertical center.
    /// </summary>
    public float CenterY => Y + Height / 2;

    /// <summary>
    /// Tests a point against the rectangle. The left and top edges are inside, and the right and bottom edges are outside.
    /// </summary>
    /// <param name="x">The horizontal position of the point.</param>
    /// <param name="y">The vertical position of the point.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;
}
