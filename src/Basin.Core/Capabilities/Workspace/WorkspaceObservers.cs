using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// A list of <see cref="IWorkspaceObserver"/> instances for an implementation of <see cref="IWorkspaceModel"/>.
/// An observer can remove itself during dispatch.
/// </summary>
public sealed class WorkspaceObservers
{
    private readonly ObserverList<IWorkspaceObserver> _observers = new();

    /// <summary>
    /// Adds an observer at the end of the list.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void Add(IWorkspaceObserver observer) => _observers.Add(observer);

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not in the list.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(IWorkspaceObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Calls <see cref="IWorkspaceObserver.OnWorkspacesChanged"/> on each observer.
    /// </summary>
    public void Changed()
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnWorkspacesChanged();
        }

        _observers.EndDispatch();
    }

    /// <summary>
    /// Calls <see cref="IWorkspaceObserver.OnWorkspaceMembersChanged"/> on each observer.
    /// </summary>
    public void MembersChanged()
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnWorkspaceMembersChanged();
        }

        _observers.EndDispatch();
    }
}
