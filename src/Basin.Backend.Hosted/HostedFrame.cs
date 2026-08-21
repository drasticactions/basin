using Basin.Capabilities;
using Basin.Scene;
using Wayland.Server;

namespace Basin.Backend.Hosted;

public sealed class HostedFrame : IDisposable
{
    private readonly WlServerDisplay _display;
    private readonly ICompositorEventLoop _loop;
    private readonly SceneOutput _output;
    private readonly OutputState _state = new();
    private bool _disposed;

    public HostedFrame(WlServerDisplay display, ICompositorEventLoop loop, SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(output);
        _display = display;
        _loop = loop;
        _output = output;
    }

    public TimeSpan DispatchWarning { get; set; } = TimeSpan.FromMilliseconds(8);

    public TimeSpan DispatchLimit { get; set; } = TimeSpan.FromMilliseconds(200);

    public event Action<TimeSpan>? DispatchOverran;

    public event Action<TimeSpan>? DispatchExceededLimit;

    public bool NeedsRepaint => _output.NeedsRepaint;

    public long Composited { get; private set; }

    public event Action<FrameTick>? BeforeDispatch;

    public event Action? WakeupRequested;

    public IFrameClock? Frames { get; set; }

    public void RequestWakeup() => WakeupRequested?.Invoke();

    public bool Tick(IRenderer renderer, IBuffer target, int age, in SceneCommitOptions options = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(target);

        var mode = _output.Output.CurrentMode;
        var tick = new FrameTick(options.TargetPresentNanos, mode.RefreshIntervalNanoseconds);

        HostedDispatch.Run(_loop, DispatchWarning, DispatchLimit, DispatchOverran, DispatchExceededLimit);

        BeforeDispatch?.Invoke(tick);
        Frames?.BeginFrame(_output.Output, options.TargetPresentNanos);

        _state.Clear();
        var hosted = options with { AllowDirectScanout = false, AllowPlaneOffload = false };
        var composited = _output.Commit(renderer, target, age, _state, hosted);
        if (composited)
        {
            Composited++;
        }

        _loop.DispatchIdle();
        _display.FlushClients();
        return composited;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _state.Dispose();
    }
}
