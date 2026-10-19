using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.UI.ImGui;

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

    public string Name => _backend.Name;

    public float FontSize => _fonts.Size;

    public int FrameIntervalMillis { get; set; }

    public UITargetKind Produces => _backend.Produces;

    public Type SurfaceContract => typeof(ImGuiUISurface);

    public IReadOnlyList<ImGuiUISurface> Surfaces => _surfaces;

    internal IImGuiBackend Backend => _backend;

    internal ImGuiFonts Fonts => _fonts;

    internal bool HasVertexOffset => _backend.HasVertexOffset;

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

    public event Action? WakeupRequested;

    public IUISurface? CreateSurface(in UISurfaceOptions options) => Create(options);

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
