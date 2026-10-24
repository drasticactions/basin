namespace Basin.Capabilities;

/// <summary>
/// A UI surface that a toolkit can put on the accessibility bus as one window.
/// </summary>
public interface IAccessibleUISurface
{
    /// <summary>
    /// Gets or sets the name that a screen reader gives the window, or <see langword="null"/> for none.
    /// </summary>
    string? AccessibleName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the surface appears on the accessibility bus while the toolkit serves one.
    /// Set it to <see langword="false"/> for a surface with nothing to read, such as a frame edge or a drag image.
    /// </summary>
    bool Accessible { get; set; }
}
