namespace Basin;

/// <summary>
/// Selects when a compositor leaves its last frame lit at exit for the next compositor on the card.
/// </summary>
public enum HandoffMode
{
    /// <summary>
    /// Retains only when a greeter under greetd exits to start a session, and restores the console otherwise.
    /// </summary>
    Auto,
    /// <summary>
    /// Retains on every orderly exit. A failure still restores.
    /// </summary>
    Retain,
    /// <summary>
    /// Never retains. The first commit at startup always does a modeset.
    /// </summary>
    Off,
}
