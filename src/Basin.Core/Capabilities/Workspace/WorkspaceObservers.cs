using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public sealed class WorkspaceObservers
{
    private readonly ObserverList<IWorkspaceObserver> _observers = new();

    public void Add(IWorkspaceObserver observer) => _observers.Add(observer);

    public void Remove(IWorkspaceObserver observer) => _observers.Remove(observer);

    public void Changed()
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnWorkspacesChanged();
        }

        _observers.EndDispatch();
    }

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
