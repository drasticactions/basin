using Basin;
using Wayland;

namespace Basin.Portal.Client;

public sealed class ClientLoop : IDisposable
{
    private readonly WlDisplay _display;
    private readonly EpollEventLoop _loop;
    private IEventSource? _displaySource;
    private bool _running;

    public ClientLoop(WlDisplay display)
    {
        _display = display;
        _loop = new EpollEventLoop();
    }

    public ICompositorEventLoop Loop => _loop;

    public event Action? Iterating;

    public void RequestFlush()
    {
        try
        {
            _display.Flush();
        }
        catch (WaylandException)
        {
        }
    }

    public void Run()
    {
        _running = true;
        _displaySource = _loop.AddFd(_display.Fd, FdReadiness.Readable, (_, events) =>
        {
            if ((events & (FdReadiness.Hangup | FdReadiness.Error)) != 0)
            {
                _running = false;
                return;
            }

            try
            {
                _display.Dispatch();
            }
            catch (WaylandException)
            {
                _running = false;
            }
        });

        while (_running)
        {
            try
            {
                _display.Flush();
            }
            catch (WaylandException)
            {
                break;
            }

            _loop.Dispatch(-1);
            Iterating?.Invoke();
        }
    }

    public void Stop() => _running = false;

    public void Dispose()
    {
        _displaySource?.Remove();
        _loop.Dispose();
    }
}
