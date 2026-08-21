using Basin.Backend.Wayland.Protocol;
using Basin.Capabilities;

namespace Basin.Backend.Wayland;

internal sealed class WaylandSeamIdle : IDisposable
{
    private readonly WaylandBackend _backend;
    private readonly IIdleSource _idle;
    private ZwpIdleInhibitorV1? _inhibitor;
    private bool _disposed;

    internal WaylandSeamIdle(WaylandBackend backend, IIdleSource idle)
    {
        _backend = backend;
        _idle = idle;
        _idle.InhibitionChanged += OnInhibitionChanged;
        OnInhibitionChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _idle.InhibitionChanged -= OnInhibitionChanged;
        Release();
    }

    private void OnInhibitionChanged()
    {
        if (_disposed)
        {
            return;
        }

        if (!_idle.IsInhibited)
        {
            Release();
            return;
        }

        if (_inhibitor is { IsDestroyed: false } ||
            _backend.ParentIdleInhibit is not { } manager ||
            _backend.Outputs.Count == 0)
        {
            return;
        }

        _inhibitor = manager.CreateInhibitor(_backend.Outputs[0].ParentSurface);
        _backend.Flush();
    }

    private void Release()
    {
        WaylandBackend.DisposeParent(_inhibitor);
        _inhibitor = null;
        _backend.Flush();
    }
}
