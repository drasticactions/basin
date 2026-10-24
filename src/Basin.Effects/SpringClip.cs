using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Selects what a <see cref="Spring"/> does when it passes <see cref="Spring.Min"/> or <see cref="Spring.Max"/>.
/// </summary>
public enum SpringClip
{
    /// <summary>
    /// Lets the spring pass the limits.
    /// </summary>
    Overshoot,

    /// <summary>
    /// Stops the spring dead at the limit.
    /// </summary>
    Clamp,

    /// <summary>
    /// Reflects the spring back from the limit.
    /// </summary>
    Bounce,
}
