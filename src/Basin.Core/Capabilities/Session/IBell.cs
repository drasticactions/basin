using Wayland.Server;

namespace Basin.Capabilities;

/// <summary>
/// Plays the system bell that <c>xdg_system_bell_v1</c> rings. The bell module calls it on each ring request.
/// </summary>
public interface IBell
{
    /// <summary>
    /// Rings the bell once.
    /// </summary>
    /// <param name="surface">The surface that the bell is for, or <see langword="null"/> when it is not for a surface.</param>
    void Ring(Surface? surface);
}
