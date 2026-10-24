using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// The <c>wl_subcompositor</c> global. It gives surfaces the <see cref="Subsurface"/> role and refuses a parent that makes a loop.
/// </summary>
public sealed class SubcompositorGlobal : IDisposable
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
        var subcompositor = new WlSubcompositorResource(client, version, id);

        subcompositor.GetSubsurface += (_, e) =>
        {
            var surface = _compositor.ResolveSurface(e.Surface);
            var parent = _compositor.ResolveSurface(e.Parent);
            if (surface is null || parent is null)
            {
                subcompositor.PostError((uint)WlSubcompositor.Error.BadSurface, "unknown surface");
                return;
            }

            if (surface == parent || IsAncestorOf(surface, parent))
            {
                subcompositor.PostError((uint)WlSubcompositor.Error.BadParent, "parent would create a loop");
                return;
            }

            if (!surface.CanSetRole(Subsurface.RoleName))
            {
                subcompositor.PostError(
                    (uint)WlSubcompositor.Error.BadSurface,
                    $"surface already has the '{surface.Role}' role");
                return;
            }

            var resource = new WlSubsurfaceResource(client, subcompositor.Version, e.Id);
            var subsurface = new Subsurface(resource, surface, parent);
            surface.TrySetRole(Subsurface.RoleName, subsurface);
        };
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
