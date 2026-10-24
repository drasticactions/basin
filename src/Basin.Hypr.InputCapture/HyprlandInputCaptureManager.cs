using System.Runtime.InteropServices;
using Basin.Eis;
using Basin.Hypr.InputCapture.Protocol;
using Wayland.Server;
using static Basin.Hypr.InputCapture.InputCaptureLog;

namespace Basin.Hypr.InputCapture;

/// <summary>
/// Advertises <c>hyprland_input_capture_manager_v1</c> and gives each session an EIS socket fd. Pointer barriers and capture state come from an <see cref="InputCaptureEngine"/>.
/// </summary>
public sealed class HyprlandInputCaptureManager : IDisposable
{
    /// <summary>
    /// The highest protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly List<HyprlandInputCaptureFront> _fronts = [];
    private readonly bool _ownsEngine;

    /// <summary>
    /// Initializes a new instance of the <see cref="HyprlandInputCaptureManager"/> class. It creates its own <see cref="InputCaptureEngine"/> and disposes it with the manager.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="loop">The loop that the engine attaches its EIS fds to.</param>
    /// <param name="layout">The layout that barriers must lie on.</param>
    /// <param name="seat">The seat whose keyboard and pointer the capture grabs.</param>
    public HyprlandInputCaptureManager(
        WlServerDisplay display,
        ICompositorEventLoop loop,
        OutputLayout layout,
        Basin.Seat.Seat seat)
        : this(display, new InputCaptureEngine(loop, layout, seat), ownsEngine: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HyprlandInputCaptureManager"/> class over a shared engine. The caller keeps ownership of <paramref name="engine"/>.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="engine">The engine that other capture fronts, for example the portal, also use.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> or <paramref name="engine"/> is <see langword="null"/>.</exception>
    public HyprlandInputCaptureManager(WlServerDisplay display, InputCaptureEngine engine)
        : this(display, engine, ownsEngine: false)
    {
    }

    private HyprlandInputCaptureManager(WlServerDisplay display, InputCaptureEngine engine, bool ownsEngine)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
        _ownsEngine = ownsEngine;
        _global = display.CreateGlobal(HyprlandInputCaptureManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets the engine that holds the sessions, the barriers and the active capture.
    /// </summary>
    public InputCaptureEngine Engine { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a barrier that is not on an output edge is refused. When it is <see langword="false"/>, the session accepts such a barrier.
    /// </summary>
    public bool EnforceBarriers
    {
        get => Engine.EnforceBarriers;
        set => Engine.EnforceBarriers = value;
    }

    /// <summary>
    /// Gets a value indicating whether a session currently holds the pointer and keyboard.
    /// </summary>
    public bool IsCaptured => Engine.IsCaptured;

    /// <summary>
    /// Gets the number of live <c>hyprland_input_capture_v1</c> sessions that this manager created.
    /// </summary>
    public int SessionCount => _fronts.Count;

    /// <summary>
    /// Occurs when a client releases the capture with a cursor position. The arguments are the layout x and y that the compositor must warp the pointer to.
    /// </summary>
    public event Action<double, double>? WarpRequested
    {
        add => Engine.WarpRequested += value;
        remove => Engine.WarpRequested -= value;
    }

    /// <summary>
    /// Feeds one pointer motion to the engine. A motion that crosses a barrier of an enabled session activates that session.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds. The engine ignores it.</param>
    /// <param name="layoutX">The new pointer x in layout coordinates.</param>
    /// <param name="layoutY">The new pointer y in layout coordinates.</param>
    /// <param name="dx">The relative x motion.</param>
    /// <param name="dy">The relative y motion.</param>
    /// <returns><see langword="true"/> when a session captured the motion and the compositor must not move its own pointer.</returns>
    public bool NotifyMotion(uint timeMs, double layoutX, double layoutY, double dx, double dy) =>
        Engine.NotifyMotion(timeMs, layoutX, layoutY, dx, dy);

    /// <summary>
    /// Ends the active capture without a client request, for example on a compositor escape chord. It deactivates and disables the active session.
    /// </summary>
    public void ForceRelease() => Engine.ForceRelease();

    /// <summary>
    /// Disposes every session and the global. It disposes the engine only when this manager created it.
    /// </summary>
    public void Dispose()
    {
        for (var i = _fronts.Count - 1; i >= 0; i--)
        {
            _fronts[i].Dispose();
        }

        _fronts.Clear();
        if (_ownsEngine)
        {
            Engine.Dispose();
        }

        _global.Dispose();
    }

    internal void Forget(HyprlandInputCaptureFront front) => _fronts.Remove(front);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new HyprlandInputCaptureManagerV1Resource(client, version, id);
        manager.CreateSession += (_, e) =>
        {
            var resource = new HyprlandInputCaptureV1Resource(client, manager.Version, e.Session);
            HyprlandInputCaptureFront front;
            try
            {
                front = new HyprlandInputCaptureFront(this, resource, e.Handle);
            }
            catch (Exception ex)
            {
                Log.Error($"session {e.Handle}: eis context failed: {ex.Message}");
                return;
            }

            _fronts.Add(front);
            int fd;
            try
            {
                fd = front.Session.Eis.AddClientFd();
            }
            catch (Exception ex)
            {
                Log.Error($"session {e.Handle}: eis client fd failed: {ex.Message}");
                return;
            }

            resource.SendEisFd(fd);
            _ = close(fd);
            Log.Info($"session {e.Handle} created");
        };
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
