namespace Basin.Shell.Nested;

/// <summary>
/// Selects where the shell puts a new window that has no parent.
/// </summary>
public enum PlacementMode
{
    /// <summary>
    /// The first position that overlaps no visible window, then the center or the next cascade step.
    /// </summary>
    Automatic,
    /// <summary>
    /// Centered on the pointer and kept inside the output.
    /// </summary>
    Pointer,
    /// <summary>
    /// Centered on the pointer, then the shell starts an interactive move.
    /// </summary>
    Manual,
    /// <summary>
    /// Maximized. A window that sets <see cref="IWindowContent.Centered"/> is centered on the work area instead.
    /// </summary>
    Maximize,
}
