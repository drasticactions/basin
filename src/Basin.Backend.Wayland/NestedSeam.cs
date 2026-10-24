using Basin.Capabilities;

namespace Basin.Backend.Wayland;

/// <summary>
/// Starts the bridges between a nested compositor and its parent: the clipboard, the primary selection, host drags and idle inhibition.
/// Construct it after <c>Freeze</c> and dispose it before the backend.
/// </summary>
public sealed class NestedSeam : IDisposable
{
    private readonly List<IDisposable> _bridges = [];
    private readonly WaylandSeamClipboard? _clipboard;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NestedSeam"/> class.
    /// A bridge starts only when its capability is given and the parent has the matching global.
    /// </summary>
    /// <param name="backend">The started backend whose parent connection the bridges use.</param>
    /// <param name="selection">The selection store to bridge, or <see langword="null"/> for no clipboard or primary selection bridge.</param>
    /// <param name="drags">The drag tracker that receives host drags, or <see langword="null"/> to ignore them.</param>
    /// <param name="idle">The idle source whose inhibition goes to the parent, or <see langword="null"/> for no idle bridge.</param>
    /// <exception cref="ArgumentNullException"><paramref name="backend"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets or sets the callback that receives the position of a host drag over an output.
    /// The arguments are the output, the time in milliseconds (0 on enter) and X and Y in output-local physical pixels.
    /// Without a clipboard bridge, the setter does nothing and the getter returns <see langword="null"/>.
    /// </summary>
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

    /// <summary>Disposes the bridges in the reverse order of their creation. A second call does nothing.</summary>
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
