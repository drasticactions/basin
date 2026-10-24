namespace Basin.Capabilities;

/// <summary>
/// Gives access to the keymap that the seat keyboard uses now.
/// </summary>
public interface IActiveKeymap
{
    /// <summary>
    /// Gets the file descriptor and the size in bytes of the active XKB text keymap.
    /// It is <see langword="null"/> when the keyboard has no keymap.
    /// </summary>
    (int Fd, uint Size)? KeymapBuffer { get; }

    /// <summary>
    /// Occurs after the active keymap changes. Read <see cref="KeymapBuffer"/> again in the handler.
    /// </summary>
    event Action? KeymapChanged;
}
