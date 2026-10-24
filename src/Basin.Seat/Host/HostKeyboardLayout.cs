using System.Text;

namespace Basin.Seat;

/// <summary>
/// Finds the keyboard layout reader for the host operating system.
/// </summary>
public static class HostKeyboardLayout
{
    /// <summary>
    /// Creates a reader for the active layout on macOS or Windows.
    /// Other hosts get no reader.
    /// </summary>
    /// <returns>A new reader that the caller owns, or <see langword="null"/> when the host has no reader or no readable layout.</returns>
    public static IHostKeyboardLayout? Detect()
    {
        if (OperatingSystem.IsMacOS())
        {
            return MacKeyboardLayout.TryCreate();
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsKeyboardLayout.TryCreate();
        }

        return null;
    }

    /// <summary>
    /// Gets the embedded US keymap in XKB text format. It is the keymap to use when the host layout cannot be read.
    /// </summary>
    public static string FallbackKeymapText => HostKeymapWriter.Fallback;
}
