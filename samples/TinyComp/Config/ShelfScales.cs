using System.Globalization;

namespace TinyComp;

internal readonly record struct ShelfScales(double Left, double Right, double Top, double Bottom)
{
    public static ShelfScales All(double scale) => new(scale, scale, scale, scale);

    public double For(CanvasSide side) => side switch
    {
        CanvasSide.Left => Left,
        CanvasSide.Right => Right,
        CanvasSide.Top => Top,
        _ => Bottom,
    };

    public string Names => string.Create(CultureInfo.InvariantCulture, $"{Left:F2},{Right:F2},{Top:F2},{Bottom:F2}");
}
