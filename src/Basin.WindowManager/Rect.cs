namespace Basin.WindowManager;

/// <summary>
/// A rectangle in the compositor's logical coordinate space. The left and top edges are inside it, the right and bottom edges are outside.
/// </summary>
/// <param name="X">The left edge in logical pixels.</param>
/// <param name="Y">The top edge in logical pixels.</param>
/// <param name="Width">The width in logical pixels.</param>
/// <param name="Height">The height in logical pixels.</param>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Gets the rectangle at the origin with zero size.
    /// </summary>
    public static Rect Empty => default;

    /// <summary>
    /// Gets a value indicating whether the rectangle covers no area, because a component of its size is zero or negative.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Gets the first column to the right of the rectangle.
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// Gets the first row below the rectangle.
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// Gets the top-left corner.
    /// </summary>
    public Point Position => new(X, Y);

    /// <summary>
    /// Gets the width and height as a <see cref="WindowManager.Size"/>.
    /// </summary>
    public Size Size => new(Width, Height);

    /// <summary>
    /// Tests whether a point is inside the rectangle. A point on the right or bottom edge is outside.
    /// </summary>
    /// <param name="point">The point to test, in the same coordinate space as the rectangle.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public bool Contains(Point point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    /// <summary>
    /// Gets the area that this rectangle and another rectangle share.
    /// </summary>
    /// <param name="other">The rectangle to intersect with.</param>
    /// <returns>The shared area, or <see cref="Empty"/> when the rectangles do not overlap.</returns>
    public Rect Intersect(Rect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= x || bottom <= y ? Empty : new Rect(x, y, right - x, bottom - y);
    }
}
