using Basin;
using Basin.Portal.Client;
using Basin.Capabilities;

namespace BasinPortal;

internal sealed class ToolkitPump : IDisposable
{
    private readonly IUIHost _host;
    private readonly ClientLoop _loop;
    private readonly IEventSource _timer;
    private bool _disposed;

    public ToolkitPump(IUIHost host, ClientLoop loop)
    {
        _host = host;
        _loop = loop;
        _timer = loop.Loop.AddTimer(Pump);
        _host.WakeupRequested += Schedule;
        loop.Iterating += Pump;
        Schedule();
    }

    public void Pump()
    {
        if (_disposed)
        {
            return;
        }

        _host.Pump();
        Schedule();
        _loop.RequestFlush();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.WakeupRequested -= Schedule;
        _loop.Iterating -= Pump;
        _timer.Remove();
    }

    private void Schedule()
    {
        if (_disposed || _timer.IsRemoved)
        {
            return;
        }

        var due = _host.NextDueMillis;
        _timer.UpdateTimer(due is null ? -1 : (int)Math.Clamp(due.Value, 0, int.MaxValue));
    }
}
