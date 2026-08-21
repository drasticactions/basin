using Basin.Capabilities;

namespace Basin.Backend.Wayland;

public sealed class NestedSeam : IDisposable
{
    private readonly List<IDisposable> _bridges = [];
    private readonly WaylandSeamClipboard? _clipboard;
    private bool _disposed;

    public NestedSeam(
        WaylandBackend backend,
        ISelectionStore? selection = null,
        IDragTracker? drags = null,
        IIdleSource? idle = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (selection is not null && backend.ParentDataDevice is { } dataDevice)
        {
            _clipboard = new WaylandSeamClipboard(backend, selection, dataDevice, drags);
            _bridges.Add(_clipboard);
        }

        if (selection is not null && backend.ParentPrimarySelectionDevice is { } primaryDevice)
        {
            _bridges.Add(new WaylandSeamPrimarySelection(backend, selection, primaryDevice));
        }

        if (idle is not null && backend.ParentIdleInhibit is not null)
        {
            _bridges.Add(new WaylandSeamIdle(backend, idle));
        }
    }

    public Action<WaylandOutput, uint, double, double>? HostDragMotion
    {
        get => _clipboard?.PointerMotion;
        set
        {
            if (_clipboard is not null)
            {
                _clipboard.PointerMotion = value;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var i = _bridges.Count - 1; i >= 0; i--)
        {
            _bridges[i].Dispose();
        }

        _bridges.Clear();
    }
}
