namespace Basin.Kinematics;

/// <summary>
/// Describes one point, line or grid that a position can snap to.
/// </summary>
/// <param name="Kind">The shape of the target.</param>
/// <param name="X">The x position of a point or a vertical line, or the x origin of a grid.</param>
/// <param name="Y">The y position of a point or a horizontal line, or the y origin of a grid.</param>
/// <param name="StepX">The horizontal grid step. A step that is not positive does not snap that axis.</param>
/// <param name="StepY">The vertical grid step. A step that is not positive does not snap that axis.</param>
public readonly record struct SnapTarget(SnapKind Kind, double X, double Y, double StepX, double StepY)
{
    /// <summary>
    /// Makes a point target.
    /// </summary>
    /// <param name="x">The horizontal position of the point.</param>
    /// <param name="y">The vertical position of the point.</param>
    /// <returns>A target of kind <see cref="SnapKind.Point"/>.</returns>
    public static SnapTarget Point(double x, double y) => new(SnapKind.Point, x, y, 0, 0);

    /// <summary>
    /// Makes a vertical line target.
    /// </summary>
    /// <param name="x">The horizontal position of the line.</param>
    /// <returns>A target of kind <see cref="SnapKind.VerticalLine"/>.</returns>
    public static SnapTarget VerticalLine(double x) => new(SnapKind.VerticalLine, x, 0, 0, 0);

    /// <summary>
    /// Makes a horizontal line target.
    /// </summary>
    /// <param name="y">The vertical position of the line.</param>
    /// <returns>A target of kind <see cref="SnapKind.HorizontalLine"/>.</returns>
    public static SnapTarget HorizontalLine(double y) => new(SnapKind.HorizontalLine, 0, y, 0, 0);

    /// <summary>
    /// Makes a grid target. One grid entry covers a lattice of any size.
    /// </summary>
    /// <param name="originX">The horizontal position of one lattice point.</param>
    /// <param name="originY">The vertical position of one lattice point.</param>
    /// <param name="stepX">The horizontal spacing.</param>
    /// <param name="stepY">The vertical spacing.</param>
    /// <returns>A target of kind <see cref="SnapKind.Grid"/>.</returns>
    public static SnapTarget Grid(double originX, double originY, double stepX, double stepY) =>
        new(SnapKind.Grid, originX, originY, stepX, stepY);

    /// <summary>
    /// Finds the point of the target nearest a position. A grid rounds each axis to the nearest lattice point, with a midpoint rounded away from zero.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <returns>The nearest point on the target.</returns>
    public (double X, double Y) Nearest(double x, double y) => Kind switch
    {
        SnapKind.Point => (X, Y),
        SnapKind.VerticalLine => (X, y),
        SnapKind.HorizontalLine => (x, Y),
        _ => (Lattice(x, X, StepX), Lattice(y, Y, StepY)),
    };

    private static double Lattice(double value, double origin, double step) =>
        step > 0 ? origin + (Math.Round((value - origin) / step, MidpointRounding.AwayFromZero) * step) : value;
}
