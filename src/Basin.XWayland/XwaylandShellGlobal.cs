using Basin.XWayland.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.XWayland;

/// <summary>
/// Advertises <c>xwayland_shell_v1</c> and pairs each X11 window serial with its <see cref="Surface"/> when the serial is committed.
/// </summary>
public sealed class XwaylandShellGlobal : IDisposable, IXwaylandShellV1Handler
{
    /// <summary>
    /// The highest protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<(WlClient Client, ulong Serial), Surface> _bySerial = [];
    private Func<WlClient, bool>? _isXwayland;

    /// <summary>
    /// Initializes a new instance of the <see cref="XwaylandShellGlobal"/> class.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="compositor">The compositor global that resolves <c>wl_surface</c> resources.</param>
    public XwaylandShellGlobal(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(XwaylandShellV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a surface commits a serial. The arguments are the Xwayland client, the 64-bit serial and the surface.
    /// </summary>
    public event Action<WlClient, ulong, Surface>? SerialCommitted;

    /// <summary>
    /// Limits the global to Xwayland clients. A later bind from another client gets a protocol error.
    /// </summary>
    /// <param name="isXwayland">The check that identifies an Xwayland client.</param>
    public void RestrictTo(Func<WlClient, bool> isXwayland) => _isXwayland = isXwayland;

    /// <summary>
    /// Destroys the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Finds the surface that committed a serial.
    /// </summary>
    /// <param name="client">The Xwayland client, or <see langword="null"/> to search all clients.</param>
    /// <param name="serial">The serial from the <c>WL_SURFACE_SERIAL</c> X11 message.</param>
    /// <returns>The surface, or <see langword="null"/> when no live surface committed the serial.</returns>
    public Surface? SurfaceFor(WlClient? client, ulong serial)
    {
        if (client is not null)
        {
            return _bySerial.GetValueOrDefault((client, serial));
        }

        foreach (var (key, surface) in _bySerial)
        {
            if (key.Serial == serial)
            {
                return surface;
            }
        }

        return null;
    }

    internal void Forget(WlClient? client, ulong serial)
    {
        if (client is not null)
        {
            _bySerial.Remove((client, serial));
            return;
        }

        foreach (var (key, _) in _bySerial)
        {
            if (key.Serial == serial)
            {
                _bySerial.Remove(key);
                return;
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        if (_isXwayland is { } check && !check(client))
        {
            var shell = new XwaylandShellV1Resource(client, version, id);
            shell.PostError(0 , "Only Xwayland can bind xwayland_shell_v1.");
            return;
        }

        _ = new XwaylandShellV1Resource(client, version, id, this);
    }

    void IXwaylandShellV1Handler.Destroy(XwaylandShellV1Resource resource)
    {
    }

    void IXwaylandShellV1Handler.GetXwaylandSurface(XwaylandShellV1Resource resource, in XwaylandShellV1Resource.GetXwaylandSurfaceEventArgs args)
    {
        var client = resource.Client;
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            _ = new XwaylandSurfaceV1Resource(client, resource.Version, args.Id);
            return;
        }

        var role = new XwaylandSurface(this, client, surface);
        _ = new XwaylandSurfaceV1Resource(client, resource.Version, args.Id, role);
        surface.Committed += role.OnCommitted;
        surface.Destroyed += role.OnSurfaceDestroyed;
    }

    private sealed class XwaylandSurface(XwaylandShellGlobal owner, WlClient client, Surface surface) : IXwaylandSurfaceV1Handler
    {
        private ulong? _pendingSerial;

        void IXwaylandSurfaceV1Handler.SetSerial(XwaylandSurfaceV1Resource resource, in XwaylandSurfaceV1Resource.SetSerialEventArgs args) =>
            _pendingSerial = ((ulong)args.SerialHi << 32) | args.SerialLo;

        void IXwaylandSurfaceV1Handler.Destroy(XwaylandSurfaceV1Resource resource)
        {
        }

        public void OnCommitted()
        {
            if (_pendingSerial is { } serial)
            {
                _pendingSerial = null;
                owner._bySerial[(client, serial)] = surface;
                owner.SerialCommitted?.Invoke(client, serial, surface);
            }
        }

        public void OnSurfaceDestroyed()
        {
            surface.Committed -= OnCommitted;
            foreach (var (key, value) in owner._bySerial)
            {
                if (value == surface)
                {
                    owner._bySerial.Remove(key);
                    break;
                }
            }
        }
    }
}
