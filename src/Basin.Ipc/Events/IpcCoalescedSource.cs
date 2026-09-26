using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

internal abstract class IpcCoalescedSource : IpcEventSource
{
    private readonly ICompositorEventLoop _loop;
    private readonly Action _flush;
    private bool _armed;

    protected IpcCoalescedSource(IpcServer server)
    {
        Server = server;
        _loop = server.Loop;
        _flush = RunFlush;
    }

    protected IpcServer Server { get; }

    protected IpcEventBus Bus => Server.Events;

    protected void Arm()
    {
        if (_armed)
        {
            return;
        }

        _armed = true;
        _loop.AddIdle(_flush);
    }

    protected abstract void Flush();

    protected abstract void Reset();

    protected override void Detach() => Reset();

    private void RunFlush()
    {
        _armed = false;
        if (!IsAttached || Server.IsDisposed)
        {
            Reset();
            return;
        }

        try
        {
            Flush();
        }
        catch (Exception exception)
        {
            Log.Error($"{GetType().Name} failed to flush: {exception}");
        }
    }
}
