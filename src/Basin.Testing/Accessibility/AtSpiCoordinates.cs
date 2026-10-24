namespace Basin.Testing;

/// <summary>
/// The coordinate space of an extents query.
/// </summary>
public enum AtSpiCoordinates : uint
{
    /// <summary>
    /// Screen coordinates.
    /// </summary>
    Screen = 0,

    /// <summary>
    /// Coordinates relative to the top-level window.
    /// </summary>
    Window = 1,

    /// <summary>
    /// Coordinates relative to the parent object.
    /// </summary>
    Parent = 2,
}
