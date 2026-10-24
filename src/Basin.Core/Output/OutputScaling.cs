namespace Basin;

/// <summary>
/// Holds the one set of rules that converts between logical and physical pixels.
/// </summary>
public static class OutputScaling
{
    /// <summary>
    /// Rounds a scale to the nearest multiple of 1/120, which is the unit of the fractional-scale protocol. The smallest result is 1/120.
    /// </summary>
    /// <param name="scale">The scale to round.</param>
    /// <returns>The rounded scale.</returns>
    public static double Snap(double scale) => Math.Max(1, Math.Round(scale * 120)) / 120.0;

    /// <summary>
    /// Gets the integer scale to advertise on <c>wl_output</c> for a fractional scale.
    /// </summary>
    /// <param name="scale">The fractional scale.</param>
    /// <returns>The snapped scale rounded up to an integer.</returns>
    public static int CeilScale(double scale) => (int)Math.Ceiling(Snap(scale) - 1e-9);

    /// <summary>
    /// Converts a logical box to physical pixels. Each edge rounds to the nearest pixel, so adjacent boxes stay adjacent.
    /// </summary>
    /// <param name="logical">The box in logical pixels.</param>
    /// <param name="scale">The logical-to-physical scale.</param>
    /// <returns>The box in physical pixels.</returns>
    public static Box ToPhysical(in Box logical, double scale)
    {
        var x1 = (int)Math.Round(logical.X * scale);
        var y1 = (int)Math.Round(logical.Y * scale);
        var x2 = (int)Math.Round((logical.X + logical.Width) * scale);
        var y2 = (int)Math.Round((logical.Y + logical.Height) * scale);
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// Converts a logical box to physical pixels and grows it to cover each pixel that it touches. At a fractional scale, it grows one more pixel on each side.
    /// </summary>
    /// <param name="logical">The box in logical pixels.</param>
    /// <param name="scale">The logical-to-physical scale.</param>
    /// <returns>The covering box in physical pixels.</returns>
    public static Box ToPhysicalExpanded(in Box logical, double scale)
    {
        var expand = scale == Math.Floor(scale) ? 0 : 1;
        var x1 = (int)Math.Floor(logical.X * scale) - expand;
        var y1 = (int)Math.Floor(logical.Y * scale) - expand;
        var x2 = (int)Math.Ceiling((logical.X + logical.Width) * scale) + expand;
        var y2 = (int)Math.Ceiling((logical.Y + logical.Height) * scale) + expand;
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// Gets the size that an output covers in the layout: its mode divided by its scale, with the axes swapped for a quarter-turn transform.
    /// </summary>
    /// <param name="output">The output to measure.</param>
    /// <returns>The size in logical pixels.</returns>
    public static (int Width, int Height) LogicalSize(this IOutput output)
    {
        var mode = output.CurrentMode;
        var width = (int)Math.Round(mode.Width / output.Scale);
        var height = (int)Math.Round(mode.Height / output.Scale);
        return output.Transform.SwapsAxes() ? (height, width) : (width, height);
    }
}
