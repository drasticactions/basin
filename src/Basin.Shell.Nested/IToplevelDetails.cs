using Basin.Capabilities;

namespace Basin.Shell.Nested;

/// <summary>
/// Supplies toplevel fields that the shell cannot know, for adopted <see cref="IWindowContent"/>.
/// The shell reads it when it publishes the window into <see cref="IToplevelModel"/>.
/// </summary>
public interface IToplevelDetails
{
    /// <summary>
    /// Gets the process id, the resource name, the app menu and the X display fields of the window.
    /// The shell copies only those fields and ignores the others.
    /// </summary>
    /// <param name="details">The details, or the default value when none are known.</param>
    /// <returns><see langword="true"/> when <paramref name="details"/> holds fields to copy.</returns>
    bool TryGetDetails(out ToplevelInfo details);
}
