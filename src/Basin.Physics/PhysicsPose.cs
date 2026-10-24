namespace Basin.Physics;

/// <summary>
/// Holds the pose of a body in a <see cref="PhysicsWorld"/>, in logical pixels. The position is the top-left of the unrotated box.
/// </summary>
/// <param name="X">The left edge of the unrotated box.</param>
/// <param name="Y">The top edge of the unrotated box.</param>
/// <param name="Angle">The rotation in radians. A positive angle turns clockwise on the screen.</param>
/// <param name="PivotX">The horizontal position of the rotation center, relative to <paramref name="X"/>.</param>
/// <param name="PivotY">The vertical position of the rotation center, relative to <paramref name="Y"/>.</param>
public readonly record struct PhysicsPose(double X, double Y, double Angle, double PivotX, double PivotY)
{
    /// <summary>
    /// Gets a value indicating whether the angle is exactly zero.
    /// </summary>
    public bool IsUpright => Angle == 0;

    /// <summary>
    /// Gets the rotation about the pivot, in the coordinates of the box. It is <see cref="RenderTransform.Identity"/> when the angle is zero.
    /// </summary>
    public RenderTransform Rotation => Angle == 0
        ? RenderTransform.Identity
        : RenderTransform.RotationAbout(Angle, PivotX, PivotY);

    /// <summary>
    /// Converts a point in world space to the unrotated box coordinates, relative to the top-left.
    /// </summary>
    /// <param name="x">The horizontal position in world space.</param>
    /// <param name="y">The vertical position in world space.</param>
    /// <returns>The point in the box coordinates.</returns>
    public (double X, double Y) ToLocal(double x, double y)
    {
        var dx = x - X - PivotX;
        var dy = y - Y - PivotY;
        var cos = Math.Cos(-Angle);
        var sin = Math.Sin(-Angle);
        return (PivotX + (cos * dx) - (sin * dy), PivotY + (sin * dx) + (cos * dy));
    }
}
