namespace Basin.Capabilities;

/// <summary>
/// A part of a frame that the pointer can be over or press.
/// </summary>
public enum FramePart
{
    /// <summary>
    /// No part. A point inside the frame that is part of nothing lets input through to the window below.
    /// </summary>
    None,
    /// <summary>
    /// The title bar. A press moves the window, and a double press toggles maximize.
    /// </summary>
    Title,
    /// <summary>
    /// The window icon.
    /// </summary>
    Icon,
    /// <summary>
    /// The window menu button.
    /// </summary>
    Menu,
    /// <summary>
    /// The close button.
    /// </summary>
    Close,
    /// <summary>
    /// The maximize button.
    /// </summary>
    Maximize,
    /// <summary>
    /// The minimize button.
    /// </summary>
    Minimize,
    /// <summary>
    /// The shade button.
    /// </summary>
    Shade,
    /// <summary>
    /// The keep-above button.
    /// </summary>
    Above,
    /// <summary>
    /// The sticky button.
    /// </summary>
    Stick,

    /// <summary>
    /// The border, where a press moves the window and does not resize it.
    /// </summary>
    Border,

    /// <summary>
    /// The top-left resize corner.
    /// </summary>
    TopLeft,
    /// <summary>
    /// The top resize edge.
    /// </summary>
    Top,
    /// <summary>
    /// The top-right resize corner.
    /// </summary>
    TopRight,
    /// <summary>
    /// The right resize edge.
    /// </summary>
    Right,
    /// <summary>
    /// The bottom-right resize corner.
    /// </summary>
    BottomRight,
    /// <summary>
    /// The bottom resize edge.
    /// </summary>
    Bottom,
    /// <summary>
    /// The bottom-left resize corner.
    /// </summary>
    BottomLeft,
    /// <summary>
    /// The left resize edge.
    /// </summary>
    Left,
}
