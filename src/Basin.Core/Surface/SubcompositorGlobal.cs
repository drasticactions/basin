using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// The <c>wl_subcompositor</c> global. It gives surfaces the <see cref="Subsurface"/> role and refuses a parent that makes a loop.
/// </summary>
public sealed class SubcompositorGlobal : IDisposable, IWlSubcompositorHandler
{
    /// <summary>The <c>wl_subcompositor</c> version that this global advertises.</summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;

    /// <summary>Initializes a new instance of the <see cref="SubcompositorGlobal"/> class and creates the global.</summary>
    /// <param name="display">The display that gets the global.</param>
    /// <param name="compositor">The compositor global that resolves surfaces.</param>
    public SubcompositorGlobal(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WlSubcompositor.Interface, Version, OnBind);
    }

    /// <summary>Destroys the global.</summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        _ = new WlSubcompositorResource(client, version, id, this);
    }

    void IWlSubcompositorHandler.Destroy(WlSubcompositorResource resource)
    {
    }

    void IWlSubcompositorHandler.GetSubsurface(WlSubcompositorResource resource, in WlSubcompositorResource.GetSubsurfaceEventArgs args)
    {
        var surface = _compositor.ResolveSurface(args.Surface);
        var parent = _compositor.ResolveSurface(args.Parent);
        if (surface is null || parent is null)
        {
            resource.PostError((uint)WlSubcompositor.Error.BadSurface, "unknown surface");
            return;
        }

        if (surface == parent || IsAncestorOf(surface, parent))
        {
            resource.PostError((uint)WlSubcompositor.Error.BadParent, "parent would create a loop");
            return;
        }

        if (!surface.CanSetRole(Subsurface.RoleName))
        {
            resource.PostError(
                (uint)WlSubcompositor.Error.BadSurface,
                $"surface already has the '{surface.Role}' role");
            return;
        }

        var subsurface = new Subsurface(resource.Client, resource.Version, args.Id, surface, parent);
        surface.TrySetRole(Subsurface.RoleName, subsurface);
    }

    private static bool IsAncestorOf(Surface surface, Surface other)
    {
        for (var role = other.SubsurfaceRole; role is not null; role = role.Parent.SubsurfaceRole)
        {
            if (role.Parent == surface)
            {
                return true;
            }
        }

        return false;
    }
}
