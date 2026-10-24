namespace Basin.Capabilities;

/// <summary>
/// The screens that a chrome toolkit can place windows on, in layout coordinates.
/// </summary>
public interface IUIScreenSource
{
    /// <summary>
    /// Gets the number of screens.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets one screen.
    /// </summary>
    /// <param name="index">The zero-based screen index.</param>
    /// <param name="info">The screen, or the default value when the index is out of range.</param>
    /// <returns><see langword="true"/> when the index is valid.</returns>
    bool TryGet(int index, out UIScreenInfo info);

    /// <summary>
    /// Occurs when a screen is added or removed, or when its mode or layout position changes.
    /// </summary>
    event Action? Changed;
}
