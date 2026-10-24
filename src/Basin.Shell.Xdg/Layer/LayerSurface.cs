using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// A <c>zwlr_layer_surface_v1</c> role object. It double-buffers the client's layer state and applies it on each commit of the <see cref="Surface"/>. Placement and configure sizes are the consumer's policy.
/// </summary>
public sealed class LayerSurface
{
    /// <summary>
    /// The role name that a layer surface sets on its <see cref="Basin.Surface"/>.
    /// </summary>
    public const string RoleName = "zwlr_layer_surface_v1";

    private readonly WlServerDisplay _display;
    private readonly ZwlrLayerSurfaceV1Resource _resource;

    private int _pendingWidth;
    private int _pendingHeight;
    private LayerAnchor _pendingAnchor;
    private int _pendingExclusiveZone;
    private LayerAnchor _pendingExclusiveEdge;
    private (int Top, int Right, int Bottom, int Left) _pendingMargin;
    private ZwlrLayerSurfaceV1.KeyboardInteractivity _pendingKeyboard;
    private LayerKind _pendingLayer;

    private readonly List<uint> _sentConfigures = [];
    private bool _configured;
    private bool _acked;
    private bool _mapped;
    private int _configureWidth;
    private int _configureHeight;
    private int _sentWidth = -1;
    private int _sentHeight = -1;
    private bool _configureScheduled;

    internal LayerSurface(
        WlServerDisplay display,
        Surface surface,
        ZwlrLayerSurfaceV1Resource resource,
        OutputGlobal? output,
        LayerKind layer,
        string clientNamespace)
    {
        _display = display;
        _resource = resource;
        Surface = surface;
        Output = output;
        Namespace = clientNamespace;
        _pendingLayer = layer;
        Layer = layer;

        resource.SetSize += (_, e) => (_pendingWidth, _pendingHeight) = ((int)e.Width, (int)e.Height);
        resource.SetAnchor += (_, e) => _pendingAnchor = (LayerAnchor)e.Anchor;
        resource.SetExclusiveZone += (_, e) => _pendingExclusiveZone = e.Zone;
        resource.SetExclusiveEdge += (_, e) => _pendingExclusiveEdge = (LayerAnchor)e.Edge;
        resource.SetMargin += (_, e) => _pendingMargin = (e.Top, e.Right, e.Bottom, e.Left);
        resource.SetKeyboardInteractivity += (_, e) => _pendingKeyboard = e.KeyboardInteractivity;
        resource.SetLayer += (_, e) => _pendingLayer = (LayerKind)e.Layer;
        resource.AckConfigure += (_, e) =>
        {
            var index = _sentConfigures.IndexOf(e.Serial);
            if (index >= 0)
            {
                _sentConfigures.RemoveRange(0, index + 1);
                _acked = true;
            }
        };
        resource.GetPopup += (_, e) =>
        {
            var popup = e.Popup is { } popupResource ? XdgPopupRegistry.Resolve(popupResource) : null;
            if (popup is not null)
            {
                popup.LayerParent = this;
                PopupAdopted?.Invoke(popup);
            }
        };
        void OnSurfaceDestroyed() => Unmap(sendClosed: false);
        resource.Destroyed += (_, _) =>
        {
            Unmap(sendClosed: false);
            surface.Committed -= OnCommitted;
            surface.Destroyed -= OnSurfaceDestroyed;
            surface.ClearRoleObject();
            Destroyed?.Invoke();
        };

        surface.Committed += OnCommitted;
        surface.Destroyed += OnSurfaceDestroyed;
    }

    /// <summary>
    /// Gets the <c>wl_surface</c> that has the layer role.
    /// </summary>
    public Surface Surface { get; }

    /// <summary>
    /// Gets or sets the output that the surface is on. It is <see langword="null"/> when the client let the compositor choose and the consumer did not set one.
    /// </summary>
    public OutputGlobal? Output { get; set; }

    /// <summary>
    /// Gets the namespace that the client gave at creation, for example <c>panel</c> or <c>wallpaper</c>.
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    /// Gets the stacking layer that the last valid commit applied. Before the first commit, it is the layer from the creation request.
    /// </summary>
    public LayerKind Layer { get; private set; }

    /// <summary>
    /// Gets the anchored edges that the last valid commit applied.
    /// </summary>
    public LayerAnchor Anchor { get; private set; }

    /// <summary>
    /// Gets the exclusive zone in logical pixels that the last valid commit applied. A positive value reserves space, 0 avoids reserved space, and a negative value ignores it.
    /// </summary>
    public int ExclusiveZone { get; private set; }

    /// <summary>
    /// Gets the edge that the exclusive zone belongs to, as the last valid commit applied. It is <see cref="LayerAnchor.None"/> when the client did not set one.
    /// </summary>
    public LayerAnchor ExclusiveEdge { get; private set; }

    /// <summary>
    /// Gets the margins in logical pixels that the last valid commit applied.
    /// </summary>
    public (int Top, int Right, int Bottom, int Left) Margin { get; private set; }

    /// <summary>
    /// Gets the keyboard interactivity that the last valid commit applied. Which layer surface gets keyboard focus is the consumer's policy.
    /// </summary>
    public ZwlrLayerSurfaceV1.KeyboardInteractivity KeyboardInteractivity { get; private set; }

    /// <summary>
    /// Gets the width in logical pixels that the client asked for at the last valid commit. A value of 0 asks the compositor to choose the width.
    /// </summary>
    public int DesiredWidth { get; private set; }

    /// <summary>
    /// Gets the height in logical pixels that the client asked for at the last valid commit. A value of 0 asks the compositor to choose the height.
    /// </summary>
    public int DesiredHeight { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the surface is mapped. It becomes true at the first commit with a buffer after an acknowledged configure.
    /// </summary>
    public bool IsMapped => _mapped;

    /// <summary>
    /// Gets a value indicating whether the client destroyed the <c>zwlr_layer_surface_v1</c> resource.
    /// </summary>
    public bool IsDestroyed => _resource.IsDestroyed;

    /// <summary>
    /// Occurs at the commit that maps the surface.
    /// </summary>
    public event Action? Mapped;

    /// <summary>
    /// Occurs when a mapped surface unmaps. A null-buffer commit, <see cref="Close"/>, and the destruction of the role object or the <c>wl_surface</c> each unmap it.
    /// </summary>
    public event Action? Unmapped;

    /// <summary>
    /// Occurs when the client destroys the role object, after <see cref="Unmapped"/>. The <c>wl_surface</c> can then take a new layer role.
    /// </summary>
    public event Action? Destroyed;

    /// <summary>
    /// Occurs after each valid commit while the surface is mapped, the mapping commit included.
    /// </summary>
    public event Action? Committed;

    /// <summary>
    /// Occurs when the client makes an <c>xdg_popup</c> a child of this surface with <c>get_popup</c>. The popup's <see cref="XdgPopupWindow.LayerParent"/> is set before the event.
    /// </summary>
    public event Action<XdgPopupWindow>? PopupAdopted;

    /// <summary>
    /// Schedules a configure with the given size for the next idle callback. Calls before the callback replace the size. A size equal to the last sent one schedules nothing.
    /// </summary>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    public void Configure(int width, int height)
    {
        if (_configured && width == _sentWidth && height == _sentHeight)
        {
            return;
        }

        _configureWidth = width;
        _configureHeight = height;
        if (_configureScheduled || _resource.IsDestroyed)
        {
            return;
        }

        _configureScheduled = true;
        _display.EventLoop.AddIdle(() =>
        {
            _configureScheduled = false;
            if (!_resource.IsDestroyed)
            {
                var serial = _display.NextSerial();
                _sentConfigures.Add(serial);
                _sentWidth = _configureWidth;
                _sentHeight = _configureHeight;
                _configured = true;
                _resource.SendConfigure(serial, (uint)_configureWidth, (uint)_configureHeight);
            }
        });
    }

    /// <summary>
    /// Sends <c>closed</c> to the client and unmaps the surface. The client must then destroy the surface.
    /// </summary>
    public void Close()
    {
        if (!_resource.IsDestroyed)
        {
            _resource.SendClosed();
        }

        Unmap(sendClosed: false);
    }

    private void OnCommitted()
    {
        if (_pendingExclusiveEdge != LayerAnchor.None &&
            (!IsSingleEdge(_pendingExclusiveEdge) || (_pendingExclusiveEdge & _pendingAnchor) == 0))
        {
            _resource.PostError(
                (uint)ZwlrLayerSurfaceV1.Error.InvalidExclusiveEdge,
                "exclusive edge is not a single edge the surface is anchored to");
            return;
        }

        DesiredWidth = _pendingWidth;
        DesiredHeight = _pendingHeight;
        Anchor = _pendingAnchor;
        ExclusiveZone = _pendingExclusiveZone;
        ExclusiveEdge = _pendingExclusiveEdge;
        Margin = _pendingMargin;
        KeyboardInteractivity = _pendingKeyboard;
        Layer = _pendingLayer;

        var hasBuffer = Surface.Current.Buffer is not null;
        if (hasBuffer && !_acked)
        {
            _resource.PostError(
                (uint)ZwlrLayerSurfaceV1.Error.InvalidSurfaceState,
                "buffer attached before the initial configure was acked");
            return;
        }

        if (!_configured && !hasBuffer)
        {
            InitialCommit?.Invoke();
            return;
        }

        if (hasBuffer && !_mapped)
        {
            _mapped = true;
            Mapped?.Invoke();
        }
        else if (!hasBuffer && _mapped)
        {
            Unmap(sendClosed: false);
            _configured = false;
            _acked = false;
            _sentConfigures.Clear();
            _sentWidth = -1;
            _sentHeight = -1;
        }

        if (_mapped)
        {
            Committed?.Invoke();
        }
    }

    /// <summary>
    /// Occurs at each commit without a buffer while no configure was sent. The consumer answers it with <see cref="Configure"/>. It occurs again after a null-buffer commit unmaps the surface.
    /// </summary>
    public event Action? InitialCommit;

    private static bool IsSingleEdge(LayerAnchor edge) => (edge & (edge - 1)) == 0;

    private void Unmap(bool sendClosed)
    {
        if (sendClosed)
        {
            Close();
            return;
        }

        if (_mapped)
        {
            _mapped = false;
            Unmapped?.Invoke();
        }
    }
}
