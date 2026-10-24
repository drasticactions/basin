namespace Basin;

/// <summary>
/// Geometry helpers for <see cref="OutputTransform"/>.
/// </summary>
public static class OutputTransforms
{
    /// <summary>
    /// Tests whether a transform exchanges the width and the height.
    /// </summary>
    /// <param name="transform">The transform to test.</param>
    /// <returns><see langword="true"/> for a 90 or 270 degree rotation, flipped or not.</returns>
    public static bool SwapsAxes(this OutputTransform transform) => ((int)transform & 1) != 0;

    /// <summary>
    /// Calculates the transform that undoes another transform.
    /// </summary>
    /// <param name="transform">The transform to undo.</param>
    /// <returns>The inverse transform. Only the 90 and 270 degree rotations change. The other transforms are their own inverse.</returns>
    public static OutputTransform Invert(this OutputTransform transform) => transform switch
    {
        OutputTransform.Rotate90 => OutputTransform.Rotate270,
        OutputTransform.Rotate270 => OutputTransform.Rotate90,
        _ => transform,
    };

    /// <summary>
    /// Combines two transforms into one transform.
    /// </summary>
    /// <param name="first">The transform to apply first.</param>
    /// <param name="second">The transform to apply second.</param>
    /// <returns>One transform with the same effect as the two in sequence.</returns>
    public static OutputTransform Compose(OutputTransform first, OutputTransform second)
    {
        var flipped = ((int)first ^ (int)second) & 4;
        var rotated = ((int)second & 4) != 0
            ? ((int)second - (int)first) & 3
            : ((int)first + (int)second) & 3;
        return (OutputTransform)(flipped | rotated);
    }

    /// <summary>
    /// Transforms a box inside an area of a given size, with the same mapping as <c>wlr_box_transform</c>.
    /// </summary>
    /// <param name="transform">The transform to apply.</param>
    /// <param name="box">The box in the coordinates before the transform.</param>
    /// <param name="width">The width of the area before the transform.</param>
    /// <param name="height">The height of the area before the transform.</param>
    /// <returns>The box in the coordinates after the transform.</returns>
    public static Box Apply(this OutputTransform transform, in Box box, int width, int height)
    {
        var w = transform.SwapsAxes() ? box.Height : box.Width;
        var h = transform.SwapsAxes() ? box.Width : box.Height;
        var (x, y) = transform switch
        {
            OutputTransform.Rotate90 => (height - box.Y - box.Height, box.X),
            OutputTransform.Rotate180 => (width - box.X - box.Width, height - box.Y - box.Height),
            OutputTransform.Rotate270 => (box.Y, width - box.X - box.Width),
            OutputTransform.Flipped => (width - box.X - box.Width, box.Y),
            OutputTransform.Flipped90 => (box.Y, box.X),
            OutputTransform.Flipped180 => (box.X, height - box.Y - box.Height),
            OutputTransform.Flipped270 => (height - box.Y - box.Height, width - box.X - box.Width),
            _ => (box.X, box.Y),
        };

        return new Box(x, y, w, h);
    }

    /// <summary>
    /// Converts a transform to a matrix that maps points of a <paramref name="width"/> by <paramref name="height"/> area.
    /// </summary>
    /// <param name="transform">The transform to convert.</param>
    /// <param name="width">The width of the area before the transform.</param>
    /// <param name="height">The height of the area before the transform.</param>
    /// <returns>The matrix. <see cref="OutputTransform.Normal"/> gives <see cref="RenderTransform.Identity"/>.</returns>
    public static RenderTransform ToMatrix(this OutputTransform transform, int width, int height) => transform switch
    {
        OutputTransform.Rotate90 => new RenderTransform(0, -1, height, 1, 0, 0, 0, 0, 1),
        OutputTransform.Rotate180 => new RenderTransform(-1, 0, width, 0, -1, height, 0, 0, 1),
        OutputTransform.Rotate270 => new RenderTransform(0, 1, 0, -1, 0, width, 0, 0, 1),
        OutputTransform.Flipped => new RenderTransform(-1, 0, width, 0, 1, 0, 0, 0, 1),
        OutputTransform.Flipped90 => new RenderTransform(0, 1, 0, 1, 0, 0, 0, 0, 1),
        OutputTransform.Flipped180 => new RenderTransform(1, 0, 0, 0, -1, height, 0, 0, 1),
        OutputTransform.Flipped270 => new RenderTransform(0, -1, height, -1, 0, width, 0, 0, 1),
        _ => RenderTransform.Identity,
    };
}
