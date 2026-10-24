namespace Basin;

/// <summary>
/// Selects when a compositor puts its chrome on the accessibility bus and serves the screen reader interfaces.
/// </summary>
public enum AccessibilityMode
{
    /// <summary>
    /// On in a full session: the DRM backend or a greeter. Off when nested or headless.
    /// </summary>
    Auto,

    /// <summary>
    /// Always on.
    /// </summary>
    On,

    /// <summary>
    /// Always off.
    /// </summary>
    Off,
}
