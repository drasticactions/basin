namespace Basin.Effects;

/// <summary>
/// Selects the launch feedback that a <see cref="StartupFeedbackEffect"/> draws beside the cursor.
/// </summary>
public enum StartupFeedbackKind
{
    /// <summary>
    /// Draws nothing.
    /// </summary>
    None = 0,

    /// <summary>
    /// Draws a square that bounces and squeezes.
    /// </summary>
    Bouncing = 1,

    /// <summary>
    /// Draws a square that cycles from black to white and back.
    /// </summary>
    Blinking = 2,

    /// <summary>
    /// Draws nothing. The consumer can show a busy cursor instead.
    /// </summary>
    Passive = 3,
}
