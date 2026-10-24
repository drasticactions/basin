using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.UI.ImGui;

/// <summary>
/// The Dear ImGui chrome host. Each surface has its own contexts, and all surfaces share one font atlas and one renderer backend. Use it on the thread that created it.
/// </summary>
public sealed class ImGuiUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IImGuiBackend _backend;
    private readonly ImGuiFonts _fonts;
    private readonly Func<long> _clock;
    private readonly List<ImGuiUISurface> _surfaces = [];
    private bool _disposed;

    internal ImGuiUIHost(IImGuiBackend backend, ImGuiHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (!ImGuiNative.TryLoad(out var declined))
        {
            throw new InvalidOperationException(declined);
        }

        options ??= new ImGuiHostOptions();
        _backend = backend;
        _clock = options.ClockNanos ?? (() => MonotonicClock.Nanos);
        FrameIntervalMillis = Math.Max(1, options.FrameIntervalMillis);
        _fonts = new ImGuiFonts(options.FontSize);
    }

    /// <summary>
    /// Gets the name of the renderer backend, "gl" or "vulkan".
    /// </summary>
    public string Name => _backend.Name;

    /// <summary>
    /// Gets the size of the shared font atlas, in logical pixels.
    /// </summary>
    public float FontSize => _fonts.Size;

    /// <summary>
    /// Gets or sets the interval in milliseconds between frames while a surface animates. The constructor clamps the option to at least 1. The setter does not clamp.
    /// </summary>
    public int FrameIntervalMillis { get; set; }

    /// <inheritdoc/>
    public UITargetKind Produces => _backend.Produces;

    /// <summary>
    /// Gets <see cref="ImGuiUISurface"/>, the type of every surface that the host creates.
    /// </summary>
    public Type SurfaceContract => typeof(ImGuiUISurface);

    /// <summary>
    /// Gets the live surfaces in the order of creation. A surface leaves the list when it is disposed.
    /// </summary>
    public IReadOnlyList<ImGuiUISurface> Surfaces => _surfaces;

    internal IImGuiBackend Backend => _backend;

    internal ImGuiFonts Fonts => _fonts;

    internal bool HasVertexOffset => _backend.HasVertexOffset;

    /// <inheritdoc/>
    public long? NextDueMillis
    {
        get
        {
            _thread.Assert();
            long? due = null;
            var now = _clock();
            for (var i = 0; i < _surfaces.Count; i++)
            {
                var surface = _surfaces[i];
                if (surface.IsDirty)
                {
                    return 0;
                }

                var interval = surface.IsAnimating ? FrameIntervalMillis
                    : surface.RefreshHz > 0 ? Math.Max(1, 1000 / surface.RefreshHz)
                    : -1;
                if (interval < 0)
                {
                    continue;
                }

                var left = Math.Max(0, interval - ((now - surface.LastFrameNanos) / 1_000_000));
                due = due is { } other ? Math.Min(other, left) : left;
            }

            return due;
        }
    }

    /// <inheritdoc/>
    public event Action? WakeupRequested;

    /// <inheritdoc/>
    public IUISurface? CreateSurface(in UISurfaceOptions options) => Create(options);

    /// <summary>
    /// Creates a configured surface with the concrete type. The caller owns the surface and must dispose it.
    /// </summary>
    /// <param name="options">The target kind, the logical size and the scale.</param>
    /// <returns>The surface, or <see langword="null"/> when the backend does not produce the kind, cannot make a target, or rejects the size.</returns>
    /// <exception cref="ObjectDisposedException">The host is disposed.</exception>
    public ImGuiUISurface? Create(in UISurfaceOptions options)
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((options.Target & _backend.Produces) == 0)
        {
            return null;
        }

        var target = _backend.CreateTarget();
        if (target is null)
        {
            return null;
        }

        var surface = new ImGuiUISurface(this, target, _fonts.Atlas, _fonts.Size, _clock);
        _surfaces.Add(surface);
        if (!surface.Configure(options.Width, options.Height, options.Scale))
        {
            surface.Dispose();
            return null;
        }

        Wake();
        return surface;
    }

    /// <summary>
    /// Draws each surface that is dirty or due. A surface that draws the same frame again publishes no buffer. The call does nothing after <see cref="Dispose"/>.
    /// </summary>
    public void Pump()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        var now = _clock();
        for (var i = _surfaces.Count - 1; i >= 0; i--)
        {
            if (i >= _surfaces.Count)
            {
                continue;
            }

            var surface = _surfaces[i];
            if (surface.IsDirty || Due(surface, now))
            {
                surface.Draw();
            }
        }
    }

    /// <summary>
    /// Disposes every surface, then the backend and the font atlas. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var surface in _surfaces.ToArray())
        {
            surface.Dispose();
        }

        _surfaces.Clear();
        _backend.Dispose();
        _fonts.Dispose();
    }

    internal void Wake()
    {
        if (!_disposed)
        {
            WakeupRequested?.Invoke();
        }
    }

    internal void Forget(ImGuiUISurface surface) => _surfaces.Remove(surface);

    private bool Due(ImGuiUISurface surface, long now)
    {
        var interval = surface.IsAnimating ? FrameIntervalMillis
            : surface.RefreshHz > 0 ? Math.Max(1, 1000 / surface.RefreshHz)
            : -1;
        return interval >= 0 && (now - surface.LastFrameNanos) / 1_000_000 >= interval - 1;
    }
}
