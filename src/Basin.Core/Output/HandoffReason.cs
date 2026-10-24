namespace Basin;

/// <summary>
/// Tells why the first commit of a DRM output did or did not keep the inherited lit state.
/// </summary>
public enum HandoffReason
{
    /// <summary>
    /// The lit state matched the wanted state, and a test commit without a modeset passed.
    /// </summary>
    Matched,
    /// <summary>
    /// The configuration asked for other timings or another HDR state, so the output did a modeset.
    /// </summary>
    Config,
    /// <summary>
    /// The kernel refused the test commit without a modeset, so the output did a modeset.
    /// </summary>
    Refused,
    /// <summary>
    /// The handoff mode is <see cref="HandoffMode.Off"/>, so the output did a modeset.
    /// </summary>
    Off,
}
