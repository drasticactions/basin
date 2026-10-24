using Wayland.Server;

namespace Basin.Capabilities.Defaults;

/// <summary>
/// A bell that makes no sound. It is the default when the compositor supplies no bell. It holds no state, so the shared instance is safe for every compositor in a process.
/// </summary>
public sealed class SilentBell : IBell
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static SilentBell Instance { get; } = new();

    /// <summary>
    /// Does nothing.
    /// </summary>
    /// <param name="surface">The surface that the bell is for. It is ignored.</param>
    public void Ring(Surface? surface)
    {
    }
}
