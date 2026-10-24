namespace Basin.Cli;

/// <summary>
/// Identifies the implementation of the Wayland wire protocol that the compositor serves clients with.
/// </summary>
public enum TransportKind
{
    /// <summary>
    /// The native libwayland-server library.
    /// </summary>
    LibWayland,

    /// <summary>
    /// The managed wire implementation in .NET.
    /// </summary>
    Managed,
}
