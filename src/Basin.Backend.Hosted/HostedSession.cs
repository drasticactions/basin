using Basin.Capabilities;
using Basin.Scene;
using Wayland.Server;

namespace Basin.Backend.Hosted;

public sealed class HostedSession : IDisposable
{
    private readonly WlServerDisplay _display;
    private readonly ICompositorEventLoop _loop;
    private readonly List<SceneOutput> _outputs = [];
    private readonly List<SceneOutput> _presented = [];
    private readonly OutputState _state = new();
    private bool _inFrame;
    private bool _disposed;

    public HostedSession(WlServerDisplay display, ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);
        _display = display;
        _loop = loop;
    }

    public TimeSpan DispatchWarning { get; set; } = TimeSpan.FromMilliseconds(8);

    public TimeSpan DispatchLimit { get; set; } = TimeSpan.FromMilliseconds(200);

    public event Action<TimeSpan>? DispatchOverran;

    public event Action<TimeSpan>? DispatchExceededLimit;

    public event Action<FrameTick>? BeforeDispatch;

    public event Action? WakeupRequested;

    public IFrameClock? Frames { get; set; }

    public void RequestWakeup() => WakeupRequested?.Invoke();

    public long Composited { get; private set; }

    public IReadOnlyList<SceneOutput> Outputs => _outputs;

    public bool NeedsRepaint
    {
        get
        {
            foreach (var output in _outputs)
            {
                if (output.NeedsRepaint)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void AddOutput(SceneOutput output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(output);
        if (_outputs.Contains(output))
        {
            throw new InvalidOperationException("The output is already registered.");
        }

        _outputs.Add(output);
    }

    public bool RemoveOutput(SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _outputs.Remove(output);
    }

    public void BeginFrame(long targetPresentNanos = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inFrame)
        {
            throw new InvalidOperationException("The previous frame was not ended.");
        }

        _inFrame = true;
        _presented.Clear();
        var refresh = _outputs.Count > 0 ? _outputs[0].Output.CurrentMode.RefreshIntervalNanoseconds : 0;
        var tick = new FrameTick(targetPresentNanos, refresh);

        HostedDispatch.Run(_loop, DispatchWarning, DispatchLimit, DispatchOverran, DispatchExceededLimit);
        BeforeDispatch?.Invoke(tick);
        if (Frames is { } frames)
        {
            foreach (var output in _outputs)
            {
                frames.BeginFrame(output.Output, targetPresentNanos);
            }
        }
    }

    public bool CommitOutput(
        SceneOutput output, IRenderer renderer, IBuffer target, int age, in SceneCommitOptions options = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(target);
        _state.Clear();
        var hosted = options with { AllowDirectScanout = false, AllowPlaneOffload = false };
        var composited = output.Commit(renderer, target, age, _state, hosted);
        if (composited)
        {
            Composited++;
            if (!_presented.Contains(output))
            {
                _presented.Add(output);
            }
        }

        return composited;
    }

    public void EndFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_inFrame)
        {
            throw new InvalidOperationException("EndFrame without a matching BeginFrame.");
        }

        _inFrame = false;
        if (Frames is { } frames)
        {
            foreach (var output in _presented)
            {
                frames.EndFrame(output.Output, 0);
            }
        }

        _presented.Clear();
        _loop.DispatchIdle();
        _display.FlushClients();
    }

    public int Tick(IRenderer renderer, Func<SceneOutput, IBuffer?> targetFor, in SceneCommitOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(targetFor);
        BeginFrame(options.TargetPresentNanos);
        var composited = 0;
        try
        {
            foreach (var output in _outputs)
            {
                if (targetFor(output) is { } target && CommitOutput(output, renderer, target, 0, options))
                {
                    composited++;
                }
            }
        }
        finally
        {
            EndFrame();
        }

        return composited;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _outputs.Clear();
        _state.Dispose();
    }
}
