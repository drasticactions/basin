namespace Basin.Capabilities;

/// <summary>
/// Speaks short messages through the screen reader, such as a workspace change or the title of a window that a key brought forward.
/// What to announce is the compositor's policy.
/// </summary>
public interface IAnnouncer
{
    /// <summary>
    /// Announces a message. The same message twice in a row is announced twice.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="priority">How urgently the reader speaks it.</param>
    void Announce(string text, AnnouncePriority priority = AnnouncePriority.Polite);
}
