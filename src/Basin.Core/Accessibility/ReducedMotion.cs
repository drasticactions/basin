namespace Basin;

/// <summary>
/// The process-wide reduced-motion switch. When it is on, every basin animation jumps to its end state:
/// effect durations read as zero, and springs and flings settle at once.
/// The compositor sets it from its appearance preferences on the loop thread.
/// </summary>
public static class ReducedMotion
{
    /// <summary>
    /// Gets or sets a value indicating whether animations jump to their end state.
    /// </summary>
    public static bool IsOn { get; set; }
}
