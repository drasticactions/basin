using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcWorkspaceEvents(IpcServer server, IpcDescribe describe, IWorkspaceModel model)
    : IpcCoalescedSource(server), IWorkspaceObserver
{
    private bool _dirty;

    public void OnWorkspacesChanged()
    {
        _dirty = true;
        Arm();
    }

    public void OnWorkspaceMembersChanged() => OnWorkspacesChanged();

    protected override void Attach() => model.AddObserver(this);

    protected override void Detach()
    {
        model.RemoveObserver(this);
        base.Detach();
    }

    protected override void Reset() => _dirty = false;

    protected override void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        Bus.Emit(IpcEventNames.WorkspaceChanged, describe.WorkspaceList(), IpcJsonContext.Default.IpcWorkspaceList);
    }
}
