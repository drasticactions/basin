using Basin;
using Basin.Diagnostics;
using Wayland;

namespace Basin.Portal.Client;

/// <summary>
/// Runs the event loop of a portal client around its Wayland display connection.
/// The loop dispatches display events and flushes requests before each wait.
/// </summary>
public sealed class ClientLoop : IDisposable
{
    private static readonly BasinLogger Log = BasinLog.For("portal-client");
    private readonly WlDisplay _display;
    private readonly EpollEventLoop _loop;
    private IEventSource? _displaySource;
    private bool _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientLoop"/> class. The loop does not own the display.
    /// </summary>
    /// <param name="display">The connected display to dispatch.</param>
    public ClientLoop(WlDisplay display)
    {
        _display = display;
        _loop = new EpollEventLoop();
    }

    /// <summary>
    /// Gets the event loop. Other services can add fds, timers and idle callbacks to it.
    /// </summary>
    public ICompositorEventLoop Loop => _loop;

    /// <summary>
    /// Occurs after each loop iteration, when the dispatch of ready sources completes.
    /// </summary>
    public event Action? Iterating;

    /// <summary>
    /// Sends the queued Wayland requests now. A connection error is ignored here and stops <see cref="Run"/> later.
    /// </summary>
    public void RequestFlush()
    {
        try
        {
            _display.Flush();
        }
        catch (WaylandException error)
        {
            Log.Debug(error, $"loop: flush failed, Run stops on the next dispatch");
        }
    }

    /// <summary>
    /// Runs the loop on the calling thread until <see cref="Stop"/> is called or the connection fails or hangs up.
    /// </summary>
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

    /// <summary>
    /// Makes <see cref="Run"/> return after the current iteration.
    /// </summary>
    public void Stop() => _running = false;

    /// <summary>
    /// Removes the display source and disposes the event loop. It does not disconnect the display.
    /// </summary>
    public void Dispose()
    {
        _displaySource?.Remove();
        _loop.Dispose();
    }
}
