namespace Basin;

/// <summary>
/// A rectangle with fractional coordinates, for example a source crop in buffer pixels.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width. A value of 0 or less makes the box empty.</param>
/// <param name="Height">The height. A value of 0 or less makes the box empty.</param>
public readonly record struct FBox(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Gets a value indicating whether the width or the height is 0 or less.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Gets the right edge, which is exclusive.
    /// </summary>
    public double Right => X + Width;

    /// <summary>
    /// Gets the bottom edge, which is exclusive.
    /// </summary>
    public double Bottom => Y + Height;

    /// <summary>
    /// Gets a value indicating whether all four components are whole numbers.
    /// </summary>
    public bool IsPixelAligned =>
        X == Math.Floor(X) && Y == Math.Floor(Y) && Width == Math.Floor(Width) && Height == Math.Floor(Height);

    /// <summary>
    /// Converts this box to the smallest integer box that contains it.
    /// </summary>
    /// <returns>An integer box with the left and top edges rounded down and the right and bottom edges rounded up.</returns>
    public Box RoundedOut()
    {
        var x1 = (int)Math.Floor(X);
        var y1 = (int)Math.Floor(Y);
        var x2 = (int)Math.Ceiling(Right);
        var y2 = (int)Math.Ceiling(Bottom);
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// Converts an integer <see cref="Basin.Box"/> to an <see cref="FBox"/> with no loss.
    /// </summary>
    /// <param name="box">The integer box to convert.</param>
    /// <returns>A fractional box with the same edges.</returns>
    public static implicit operator FBox(Box box) => new(box.X, box.Y, box.Width, box.Height);
}
