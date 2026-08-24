using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public sealed class LayerSurface
{
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

    public Surface Surface { get; }

    public OutputGlobal? Output { get; set; }

    public string Namespace { get; }

    public LayerKind Layer { get; private set; }

    public LayerAnchor Anchor { get; private set; }

    public int ExclusiveZone { get; private set; }

    public LayerAnchor ExclusiveEdge { get; private set; }

    public (int Top, int Right, int Bottom, int Left) Margin { get; private set; }

    public ZwlrLayerSurfaceV1.KeyboardInteractivity KeyboardInteractivity { get; private set; }

    public int DesiredWidth { get; private set; }

    public int DesiredHeight { get; private set; }

    public bool IsMapped => _mapped;

    public bool IsDestroyed => _resource.IsDestroyed;

    public event Action? Mapped;

    public event Action? Unmapped;

    public event Action? Destroyed;

    public event Action? Committed;

    public event Action<XdgPopupWindow>? PopupAdopted;

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
