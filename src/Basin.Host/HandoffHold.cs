namespace Basin.Host;

/// <summary>
/// Identifies what starts the clock of a handoff transition. Until then the inherited frame stays at full opacity. <see cref="OutputDriver.HandoffHoldLimit"/> starts the clock in all cases.
/// </summary>
public enum HandoffHold
{
    /// <summary>
    /// The clock starts at the first repaint after every primary output has rendered once.
    /// </summary>
    Ready,
    /// <summary>
    /// The clock starts at the first repaint after <see cref="OutputDriver.ReleaseHandoff"/>.
    /// </summary>
    Signal,
}
