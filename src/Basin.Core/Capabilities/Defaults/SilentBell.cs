using Wayland.Server;

namespace Basin.Capabilities.Defaults;

/// <summary>
/// A bell that makes no sound and counts the rings. It is the default when the compositor supplies no bell.
/// </summary>
public sealed class SilentBell : IBell
{
    /// <summary>
    /// Gets the shared instance. Its ring count is shared by every user.
    /// </summary>
    public static SilentBell Instance { get; } = new();

    /// <summary>
    /// Gets the number of times that the bell rang.
    /// </summary>
    public int Rings { get; private set; }

    /// <summary>
    /// Counts a ring and makes no sound.
    /// </summary>
    /// <param name="surface">The surface that the bell is for. It is ignored.</param>
    public void Ring(Surface? surface) => Rings++;
}
