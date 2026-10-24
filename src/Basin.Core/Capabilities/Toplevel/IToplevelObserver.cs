using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// Receives the add, change and remove notifications of toplevels. The ids are in the id space of the sender.
/// </summary>
public interface IToplevelObserver
{
    /// <summary>
    /// The sender calls this method after a new toplevel appears. A lookup by the id succeeds during the call.
    /// </summary>
    /// <param name="toplevelId">The id of the new toplevel.</param>
    void OnToplevelAdded(ulong toplevelId);

    /// <summary>
    /// The sender calls this method after a value in the <see cref="ToplevelInfo"/> of the toplevel changes, for example its title, state or geometry.
    /// </summary>
    /// <param name="toplevelId">The id of the changed toplevel.</param>
    void OnToplevelChanged(ulong toplevelId);

    /// <summary>
    /// The sender calls this method after a toplevel goes away. The id is not valid after the call.
    /// </summary>
    /// <param name="toplevelId">The id of the removed toplevel.</param>
    void OnToplevelRemoved(ulong toplevelId);
}
