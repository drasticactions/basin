using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <c>zwlr_layer_shell_v1</c> global. It makes a <see cref="LayerSurface"/> for each valid <c>get_layer_surface</c> request and raises <see cref="NewSurface"/>.
/// </summary>
public sealed class LayerShell : IDisposable
{
    /// <summary>
    /// The version of <c>zwlr_layer_shell_v1</c> that the global advertises.
    /// </summary>
    public const int Version = 5;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly WlServerDisplay _display;

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerShell"/> class. It creates the global on <paramref name="display"/> at once.
    /// </summary>
    /// <param name="display">The display that advertises the global.</param>
    /// <param name="compositor">The compositor global that resolves the client's surfaces.</param>
    public LayerShell(WlServerDisplay display, CompositorGlobal compositor)
    {
        _display = display;
        _compositor = compositor;
        _global = display.CreateGlobal(ZwlrLayerShellV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a client gets a layer surface for a <c>wl_surface</c> without a conflicting role. The argument is the new surface, before its first commit.
    /// </summary>
    public event Action<LayerSurface>? NewSurface;

    /// <summary>
    /// Destroys the global. Layer surfaces that exist keep their resources.
    /// </summary>
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
