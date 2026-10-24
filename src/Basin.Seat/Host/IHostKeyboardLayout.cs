using System.Text;

namespace Basin.Seat;

/// <summary>
/// Reads the active keyboard layout of the host operating system as XKB keymap text.
/// </summary>
public interface IHostKeyboardLayout
{
    /// <summary>
    /// Gets the name of the active layout. The keymap text uses it as the symbols name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Reads the active layout and writes it as XKB keymap text.
    /// </summary>
    /// <param name="xkb">The keymap text, or an empty string when the read fails.</param>
    /// <returns><see langword="false"/> when the layout cannot be read.</returns>
    bool TryReadKeymapText(out string xkb);

    /// <summary>
    /// Occurs when the host changes to a different layout.
    /// It can occur on a thread other than the compositor thread.
    /// </summary>
    event Action? Changed;
}
