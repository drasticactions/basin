namespace Basin.Capabilities;

/// <summary>
/// How urgently a screen reader speaks an announcement.
/// </summary>
public enum AnnouncePriority
{
    /// <summary>
    /// After the reader finishes what it is saying.
    /// </summary>
    Polite,

    /// <summary>
    /// At once, interrupting the reader.
    /// </summary>
    Assertive,
}
