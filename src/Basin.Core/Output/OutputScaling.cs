namespace Basin;

public static class OutputScaling
{
    public static double Snap(double scale) => Math.Max(1, Math.Round(scale * 120)) / 120.0;

    public static int CeilScale(double scale) => (int)Math.Ceiling(Snap(scale) - 1e-9);

    public static Box ToPhysical(in Box logical, double scale)
    {
        var x1 = (int)Math.Round(logical.X * scale);
        var y1 = (int)Math.Round(logical.Y * scale);
        var x2 = (int)Math.Round((logical.X + logical.Width) * scale);
        var y2 = (int)Math.Round((logical.Y + logical.Height) * scale);
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }

    public static Box ToPhysicalExpanded(in Box logical, double scale)
    {
        var expand = scale == Math.Floor(scale) ? 0 : 1;
        var x1 = (int)Math.Floor(logical.X * scale) - expand;
        var y1 = (int)Math.Floor(logical.Y * scale) - expand;
        var x2 = (int)Math.Ceiling((logical.X + logical.Width) * scale) + expand;
        var y2 = (int)Math.Ceiling((logical.Y + logical.Height) * scale) + expand;
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }

    public static (int Width, int Height) LogicalSize(this IOutput output)
    {
        var box = output.ContentBox();
        return ((int)Math.Round(box.Width / output.Scale), (int)Math.Round(box.Height / output.Scale));
    }

    public static Box ContentBox(this IOutput output)
    {
        var mode = output.CurrentMode;
        var (width, height) = output.Transform.SwapsAxes()
            ? (mode.Height, mode.Width)
            : (mode.Width, mode.Height);
        var aspect = output.AspectRatio;
        if (aspect <= 0 || width <= 0 || height <= 0)
        {
            return new Box(0, 0, width, height);
        }

        var fitWidth = Math.Min(width, (int)Math.Round(height * aspect));
        var fitHeight = Math.Min(height, (int)Math.Round(width / aspect));
        return new Box((width - fitWidth) / 2, (height - fitHeight) / 2, fitWidth, fitHeight);
    }
}
