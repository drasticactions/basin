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
        var mode = output.CurrentMode;
        var width = (int)Math.Round(mode.Width / output.Scale);
        var height = (int)Math.Round(mode.Height / output.Scale);
        return output.Transform.SwapsAxes() ? (height, width) : (width, height);
    }
}
