namespace Basin.Kinematics;

/// <summary>
/// Names the shape of a <see cref="SnapTarget"/>.
/// </summary>
public enum SnapKind
{
    /// <summary>A single point.</summary>
    Point,
    /// <summary>A vertical line at one x position. A snap keeps the y position.</summary>
    VerticalLine,
    /// <summary>A horizontal line at one y position. A snap keeps the x position.</summary>
    HorizontalLine,
    /// <summary>A lattice of points from an origin at a fixed step on each axis.</summary>
    Grid,
}
