using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.UI.Quill;

namespace Basin.UI.Paper;

public sealed class PaperUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IUIHost _inner;
    private readonly bool _ownsInner;
    private readonly Func<long> _clock;
    private readonly List<PaperSurface> _surfaces = [];
    private long _lastPump;
    private bool _disposed;

    public PaperUIHost(IUIHost inner, bool ownsInner = false, Func<long>? clockNanos = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (!typeof(IQuillUISurface).IsAssignableFrom(inner.SurfaceContract))
        {
            throw new ArgumentException($"a Paper host needs Quill surfaces, and this host makes {inner.SurfaceContract.Name}.", nameof(inner));
        }

        _inner = inner;
        _ownsInner = ownsInner;
        _clock = clockNanos ?? (() => MonotonicClock.Nanos);
        _inner.WakeupRequested += OnInnerWakeup;
    }

    public IUIHost Inner => _inner;

    public int FrameIntervalMillis { get; set; } = 16;

    public UITargetKind Produces => _inner.Produces;

    public Type SurfaceContract => typeof(PaperSurface);

    public IReadOnlyList<PaperSurface> Surfaces => _surfaces;

    public long? NextDueMillis
    {
        get
        {
            _thread.Assert();
            long? due = _inner.NextDueMillis;
            foreach (var surface in _surfaces)
            {
                if (surface.IsDirty)
                {
                    return 0;
                }

                if (surface.IsMoving)
                {
                    var left = Math.Max(0, FrameIntervalMillis - ((_clock() - _lastPump) / 1_000_000));
                    due = due is { } other ? Math.Min(other, left) : left;
                }
            }

            return due;
        }
    }

    public event Action? WakeupRequested;

    public IUISurface? CreateSurface(in UISurfaceOptions options) => Create(options);

    public PaperSurface? Create(in UISurfaceOptions options)
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var created = _inner.CreateSurface(options);
        if (created is not IQuillUISurface quill)
        {
            created?.Dispose();
            return null;
        }

        var surface = new PaperSurface(quill, _clock, this);
        _surfaces.Add(surface);
        Wake();
        return surface;
    }

    public void Pump()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _inner.Pump();
        _lastPump = _clock();
        for (var i = _surfaces.Count - 1; i >= 0; i--)
        {
            if (i >= _surfaces.Count)
            {
                continue;
            }

            var surface = _surfaces[i];
            if (surface.IsDirty || surface.IsMoving)
            {
                surface.Draw();
            }
        }
    }

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.WakeupRequested -= OnInnerWakeup;
        foreach (var surface in _surfaces.ToArray())
        {
            surface.Dispose();
        }

        _surfaces.Clear();
        if (_ownsInner)
        {
            _inner.Dispose();
        }
    }

    internal void Wake()
    {
        if (!_disposed)
        {
            WakeupRequested?.Invoke();
        }
    }

    internal void Forget(PaperSurface surface) => _surfaces.Remove(surface);

    private void OnInnerWakeup() => WakeupRequested?.Invoke();
}
