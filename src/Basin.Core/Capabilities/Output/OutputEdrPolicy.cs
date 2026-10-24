namespace Basin.Capabilities;

/// <summary>
/// When an SDR panel can raise its backlight above the brightness setting to show extended dynamic range content.
/// </summary>
public enum OutputEdrPolicy
{
    /// <summary>
    /// The backlight stays at the brightness setting.
    /// </summary>
    Never = 0,
    /// <summary>
    /// The backlight can rise when content asks for headroom.
    /// </summary>
    Always = 1,
}
