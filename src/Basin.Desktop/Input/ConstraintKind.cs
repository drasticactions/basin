using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Identifies the kind of pointer constraint that a client requested.
/// </summary>
public enum ConstraintKind
{
    /// <summary>
    /// The pointer stays at one position, from <c>zwp_locked_pointer_v1</c>.
    /// </summary>
    Lock,
    /// <summary>
    /// The pointer can move only inside a region, from <c>zwp_confined_pointer_v1</c>.
    /// </summary>
    Confine,
}
