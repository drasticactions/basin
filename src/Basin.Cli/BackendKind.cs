namespace Basin.Cli;

/// <summary>
/// Identifies where a compositor sends its outputs.
/// </summary>
public enum BackendKind
{
    /// <summary>
    /// The outputs are windows inside a parent Wayland session.
    /// </summary>
    Nested,

    /// <summary>
    /// The outputs are KMS connectors that the compositor drives directly.
    /// </summary>
    Drm,

    /// <summary>
    /// The outputs exist only in memory and nothing shows them.
    /// </summary>
    Headless,
}
