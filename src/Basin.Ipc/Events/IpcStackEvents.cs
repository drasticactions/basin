using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcStackEvents(IpcServer server, IpcDescribe describe, IToplevelStack stack)
    : IpcCoalescedSource(server), IToplevelStackObserver
{
    private bool _dirty;

    public void OnToplevelStackChanged()
    {
        _dirty = true;
        Arm();
    }

    protected override void Attach() => stack.AddObserver(this);

    protected override void Detach()
    {
        stack.RemoveObserver(this);
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
        Bus.Emit(IpcEventNames.StackChanged, describe.StackIds(), IpcJsonContext.Default.IpcStack);
    }
}
