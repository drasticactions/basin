using Basin.XWayland.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.XWayland;

public sealed class XwaylandShellGlobal : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<(WlClient Client, ulong Serial), Surface> _bySerial = [];
    private Func<WlClient, bool>? _isXwayland;

    public XwaylandShellGlobal(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(XwaylandShellV1.Interface, Version, OnBind);
    }

    public event Action<WlClient, ulong, Surface>? SerialCommitted;

    public void RestrictTo(Func<WlClient, bool> isXwayland) => _isXwayland = isXwayland;

    public void Dispose() => _global.Dispose();

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
        var shell = new XwaylandShellV1Resource(client, version, id);
        if (_isXwayland is { } check && !check(client))
        {
            shell.PostError(0 , "only Xwayland may bind xwayland_shell_v1");
            return;
        }

        shell.GetXwaylandSurface += (_, e) =>
        {
            var resource = new XwaylandSurfaceV1Resource(client, shell.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            ulong? pendingSerial = null;
            resource.SetSerial += (_, se) => pendingSerial = ((ulong)se.SerialHi << 32) | se.SerialLo;

            void OnCommitted()
            {
                if (pendingSerial is { } serial)
                {
                    pendingSerial = null;
                    _bySerial[(client, serial)] = surface;
                    SerialCommitted?.Invoke(client, serial, surface);
                }
            }

            surface.Committed += OnCommitted;
            surface.Destroyed += () =>
            {
                surface.Committed -= OnCommitted;
                foreach (var (key, value) in _bySerial)
                {
                    if (value == surface)
                    {
                        _bySerial.Remove(key);
                        break;
                    }
                }
            };
        };
    }
}
