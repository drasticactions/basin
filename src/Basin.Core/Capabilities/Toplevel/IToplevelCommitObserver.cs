namespace Basin.Capabilities;

/// <summary>
/// Receives the content commits of toplevels from an <see cref="IToplevelModel"/> or an <see cref="IToplevelSource"/>.
/// </summary>
public interface IToplevelCommitObserver
{
    /// <summary>
    /// The sender calls this method after a commit changes the content of a toplevel surface or of one of its subsurfaces.
    /// A commit with empty damage does not cause a call.
    /// </summary>
    /// <param name="toplevelId">The toplevel id, in the id space of the sender.</param>
    /// <param name="damage">The damaged area in the surface coordinates of the root surface of the toplevel.</param>
    void OnToplevelCommitted(ulong toplevelId, in Box damage);
}
