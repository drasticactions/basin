namespace Basin;

/// <summary>
/// The rotation and flip of an output or a buffer. The values match <c>wl_output.transform</c>, and rotation is counterclockwise.
/// </summary>
public enum OutputTransform
{
    /// <summary>
    /// No rotation and no flip.
    /// </summary>
    Normal = 0,
    /// <summary>
    /// A rotation of 90 degrees.
    /// </summary>
    Rotate90 = 1,
    /// <summary>
    /// A rotation of 180 degrees.
    /// </summary>
    Rotate180 = 2,
    /// <summary>
    /// A rotation of 270 degrees.
    /// </summary>
    Rotate270 = 3,
    /// <summary>
    /// A flip around the vertical axis.
    /// </summary>
    Flipped = 4,
    /// <summary>
    /// A flip around the vertical axis, then a rotation of 90 degrees.
    /// </summary>
    Flipped90 = 5,
    /// <summary>
    /// A flip around the vertical axis, then a rotation of 180 degrees.
    /// </summary>
    Flipped180 = 6,
    /// <summary>
    /// A flip around the vertical axis, then a rotation of 270 degrees.
    /// </summary>
    Flipped270 = 7,
}
