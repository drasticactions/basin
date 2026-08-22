namespace Basin;

public static class OutputTransforms
{
    public static bool SwapsAxes(this OutputTransform transform) => ((int)transform & 1) != 0;

    public static OutputTransform Invert(this OutputTransform transform) => transform switch
    {
        OutputTransform.Rotate90 => OutputTransform.Rotate270,
        OutputTransform.Rotate270 => OutputTransform.Rotate90,
        _ => transform,
    };

    public static OutputTransform Compose(OutputTransform first, OutputTransform second)
    {
        var flipped = ((int)first ^ (int)second) & 4;
        var rotated = ((int)second & 4) != 0
            ? ((int)second - (int)first) & 3
            : ((int)first + (int)second) & 3;
        return (OutputTransform)(flipped | rotated);
    }

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
