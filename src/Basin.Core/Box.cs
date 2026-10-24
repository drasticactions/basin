namespace Basin;

/// <summary>
/// An integer rectangle in a coordinate space that the caller defines. The right and bottom edges are exclusive.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width. A value of 0 or less makes the box empty.</param>
/// <param name="Height">The height. A value of 0 or less makes the box empty.</param>
public readonly record struct Box(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Gets a value indicating whether the width or the height is 0 or less.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Gets the right edge, which is exclusive.
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// Gets the bottom edge, which is exclusive.
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// Tests whether a point is inside this box. A point on the right or bottom edge is outside.
    /// </summary>
    /// <param name="p">The point to test.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public bool Contains(Point p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;

    /// <summary>
    /// Tests whether another box is fully inside this box. An empty box is always inside.
    /// </summary>
    /// <param name="other">The box to test.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> is empty or fully inside.</returns>
    public bool Contains(in Box other) =>
        other.IsEmpty || (other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom);

    /// <summary>
    /// Moves this box and keeps its size.
    /// </summary>
    /// <param name="dx">The horizontal offset.</param>
    /// <param name="dy">The vertical offset.</param>
    /// <returns>The moved box.</returns>
    public Box Translated(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>
    /// Calculates the overlap of this box and another box.
    /// </summary>
    /// <param name="other">The other box.</param>
    /// <returns>The overlap. Its width or height is 0 when the boxes do not overlap.</returns>
    public Box Intersect(in Box other)
    {
        var x1 = Math.Max(X, other.X);
        var y1 = Math.Max(Y, other.Y);
        var x2 = Math.Min(Right, other.Right);
        var y2 = Math.Min(Bottom, other.Bottom);
        return new Box(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }
}
