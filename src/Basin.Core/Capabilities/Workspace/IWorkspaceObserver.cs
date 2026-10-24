using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// Receives change notifications from an <see cref="IWorkspaceModel"/>. Each call tells the observer to read the model again.
/// </summary>
public interface IWorkspaceObserver
{
    /// <summary>
    /// The model calls this method after a group or a workspace is added or removed, or after a workspace changes.
    /// </summary>
    void OnWorkspacesChanged();

    /// <summary>
    /// The model calls this method after the members of a workspace change.
    /// </summary>
    void OnWorkspaceMembersChanged();
}
