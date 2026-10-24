using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.UI.Quill;

namespace Basin.UI.Paper;

/// <summary>
/// An <see cref="IUIHost"/> that draws Prowl.Paper user interfaces on the Quill surfaces of an inner host.
/// Use it on the thread that made it.
/// </summary>
public sealed class PaperUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IUIHost _inner;
    private readonly bool _ownsInner;
    private readonly Func<long> _clock;
    private readonly List<PaperSurface> _surfaces = [];
    private long _lastPump;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaperUIHost"/> class.
    /// </summary>
    /// <param name="inner">The host that makes the Quill surfaces.</param>
    /// <param name="ownsInner"><see langword="true"/> to dispose <paramref name="inner"/> with this host.</param>
    /// <param name="clockNanos">The monotonic clock in nanoseconds, or <see langword="null"/> for <see cref="MonotonicClock.Nanos"/>.</param>
    /// <exception cref="ArgumentException">The surfaces of <paramref name="inner"/> are not <see cref="IQuillUISurface"/>.</exception>
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

    /// <summary>
    /// Gets the host that makes the Quill surfaces.
    /// </summary>
    public IUIHost Inner => _inner;

    /// <summary>
    /// Gets or sets the time in milliseconds between two frames of a surface that animates. The default is 16.
    /// </summary>
    public int FrameIntervalMillis { get; set; } = 16;

    /// <summary>
    /// Gets the buffer kinds of the inner host.
    /// </summary>
    public UITargetKind Produces => _inner.Produces;

    /// <summary>
    /// Gets the surface contract. It is <see cref="PaperSurface"/>.
    /// </summary>
    public Type SurfaceContract => typeof(PaperSurface);

    /// <summary>
    /// Gets the surfaces of this host that are not disposed.
    /// </summary>
    public IReadOnlyList<PaperSurface> Surfaces => _surfaces;

    /// <summary>
    /// Gets the delay in milliseconds until the next <see cref="Pump"/>. It is 0 when a surface is dirty.
    /// An animating surface makes it at most <see cref="FrameIntervalMillis"/> after the last pump.
    /// </summary>
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

    /// <summary>
    /// Occurs when the inner host asks for a pump, when a surface is created and when a surface becomes dirty.
    /// </summary>
    public event Action? WakeupRequested;

    /// <summary>
    /// Creates a <see cref="PaperSurface"/> on a new Quill surface of the inner host. It is the same as <see cref="Create"/>.
    /// </summary>
    /// <param name="options">The target kind, the logical size and the scale.</param>
    /// <returns>The surface, or <see langword="null"/> when the inner host makes no Quill surface.</returns>
    /// <exception cref="ObjectDisposedException">The host is disposed.</exception>
    public IUISurface? CreateSurface(in UISurfaceOptions options) => Create(options);

    /// <summary>
    /// Creates a <see cref="PaperSurface"/> on a new Quill surface of the inner host. The caller owns it and must dispose it.
    /// </summary>
    /// <param name="options">The target kind, the logical size and the scale.</param>
    /// <returns>The surface, or <see langword="null"/> when the inner host makes no Quill surface.</returns>
    /// <exception cref="ObjectDisposedException">The host is disposed.</exception>
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

    /// <summary>
    /// Pumps the inner host, then draws each surface that is dirty or animates. It does nothing after dispose.
    /// </summary>
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

    /// <summary>
    /// Disposes every surface, and the inner host when this host owns it. A second call does nothing.
    /// </summary>
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
