namespace Basin.Capabilities;

/// <summary>
/// The effects that the compositor can draw behind a translucent surface. A combined value means that each named effect is available.
/// </summary>
[Flags]
public enum BackgroundEffects : uint
{
    /// <summary>
    /// No background effect is available.
    /// </summary>
    None = 0,

    /// <summary>
    /// The compositor can blur what is behind a surface.
    /// </summary>
    Blur = 1,

    /// <summary>
    /// The compositor can change the contrast, intensity and saturation of what is behind a surface.
    /// </summary>
    Contrast = 2,
}
