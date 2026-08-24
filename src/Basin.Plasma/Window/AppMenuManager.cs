using Basin.Plasma.Protocol;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class AppMenuManager : IDisposable
{
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, SurfaceAppMenu> _menus = [];

    public AppMenuManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _global = display.CreateGlobal(OrgKdeKwinAppmenuManager.Interface, Version, OnBind);
    }

    public event Action<Surface, string, string>? AddressChanged;

    public SurfaceAppMenu? MenuOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return _menus.GetValueOrDefault(surface);
    }

    public void Dispose()
    {
        _menus.Clear();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinAppmenuManagerResource(client, version, id);
        manager.Create += (_, e) =>
        {
            var resource = new OrgKdeKwinAppmenuResource(client, manager.Version, e.Id);
            if (_compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            void Clear()
            {
                if (_menus.Remove(surface))
                {
                    AddressChanged?.Invoke(surface, string.Empty, string.Empty);
                }
            }

            resource.SetAddress += (_, ae) =>
            {
                _menus[surface] = new SurfaceAppMenu(surface, ae.ServiceName, ae.ObjectPath);
                AddressChanged?.Invoke(surface, ae.ServiceName, ae.ObjectPath);
            };
            resource.Destroyed += (_, _) => Clear();
            surface.Destroyed += Clear;
        };
    }
}
