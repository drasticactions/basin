using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public sealed class LayerShell : IDisposable
{
    public const int Version = 5;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly WlServerDisplay _display;

    public LayerShell(WlServerDisplay display, CompositorGlobal compositor)
    {
        _display = display;
        _compositor = compositor;
        _global = display.CreateGlobal(ZwlrLayerShellV1.Interface, Version, OnBind);
    }

    public event Action<LayerSurface>? NewSurface;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new ZwlrLayerShellV1Resource(client, version, id);
        resource.GetLayerSurface += (_, e) =>
        {
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                resource.PostError((uint)ZwlrLayerShellV1.Error.Role, "unknown wl_surface");
                return;
            }

            var layerResource = new ZwlrLayerSurfaceV1Resource(client, resource.Version, e.Id);
            if (!surface.CanSetRole(LayerSurface.RoleName))
            {
                layerResource.PostError((uint)ZwlrLayerShellV1.Error.Role, "surface already has a role");
                return;
            }

            var output = OutputGlobal.FromResource(e.Output);
            var layerSurface = new LayerSurface(_display, surface, layerResource, output, (LayerKind)e.Layer, e.Namespace);
            surface.TrySetRole(LayerSurface.RoleName, layerSurface);
            NewSurface?.Invoke(layerSurface);
        };
    }
}
