using Wayland.Server;

namespace Basin.Hypr;

/// <summary>
/// Decides which clients can register gamescope action-binding triggers as global shortcuts.
/// </summary>
public interface IActionBindingPolicy
{
    /// <summary>
    /// Decides once, when a client binds the manager, whether its bindings can register triggers. A refused client can create and arm bindings, but they never fire.
    /// </summary>
    /// <param name="client">The client that bound the global.</param>
    /// <returns><see langword="true"/> when the triggers of the client go into the shortcut registry.</returns>
    bool Allows(WlClient client);
}
