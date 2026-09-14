using System.Runtime.InteropServices;
using Basin.Eis;
using Basin.Hypr.InputCapture.Protocol;
using Wayland.Server;
using static Basin.Hypr.InputCapture.InputCaptureLog;

namespace Basin.Hypr.InputCapture;

public sealed class HyprlandInputCaptureManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly List<HyprlandInputCaptureFront> _fronts = [];
    private readonly bool _ownsEngine;

    public HyprlandInputCaptureManager(
        WlServerDisplay display,
        ICompositorEventLoop loop,
        OutputLayout layout,
        Basin.Seat.Seat seat)
        : this(display, new InputCaptureEngine(loop, layout, seat), ownsEngine: true)
    {
    }

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

    public InputCaptureEngine Engine { get; }

    public bool EnforceBarriers
    {
        get => Engine.EnforceBarriers;
        set => Engine.EnforceBarriers = value;
    }

    public bool IsCaptured => Engine.IsCaptured;

    public int SessionCount => _fronts.Count;

    public event Action<double, double>? WarpRequested
    {
        add => Engine.WarpRequested += value;
        remove => Engine.WarpRequested -= value;
    }

    public bool NotifyMotion(uint timeMs, double layoutX, double layoutY, double dx, double dy) =>
        Engine.NotifyMotion(timeMs, layoutX, layoutY, dx, dy);

    public void ForceRelease() => Engine.ForceRelease();

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
