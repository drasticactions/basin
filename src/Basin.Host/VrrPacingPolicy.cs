namespace Basin.Host;

/// <summary>
/// Identifies when an output paces repaints on content arrival instead of the vblank.
/// </summary>
public enum VrrPacingPolicy
{
    /// <summary>
    /// Arrival pacing applies with tearing, or with adaptive sync while a surface is a scanout candidate or scanned out directly.
    /// </summary>
    Fullscreen,
    /// <summary>
    /// Arrival pacing applies whenever the output has adaptive sync or the frame tears.
    /// </summary>
    Always,
    /// <summary>
    /// Repaints always follow the vblank.
    /// </summary>
    Never,
}
