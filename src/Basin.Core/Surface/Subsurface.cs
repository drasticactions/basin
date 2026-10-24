using Basin.Diagnostics;
using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// The <c>wl_subsurface</c> role. Position and stacking changes apply on the parent's commit, and a synchronized child's own commits apply there too.
/// </summary>
public sealed class Subsurface : IWlSubsurfaceHandler
{
    /// <summary>The role name that <see cref="Surface.Role"/> holds for a subsurface.</summary>
    public const string RoleName = "wl_subsurface";

    private int _pendingX;
    private int _pendingY;
    private bool _hasPendingPosition;
    private bool _destroyed;

    internal Subsurface(WlClient client, uint version, uint id, Surface surface, Surface parent)
    {
        Resource = new WlSubsurfaceResource(client, version, id, this);
        Surface = surface;
        Parent = parent;
        BasinCounters.Track();

        surface.SubsurfaceRole = this;

        parent.SubsurfacesAbove.Add(this);

        Resource.Destroyed += (_, _) => OnRoleDestroyed();
    }

    void IWlSubsurfaceHandler.Destroy(WlSubsurfaceResource resource)
    {
    }

    void IWlSubsurfaceHandler.SetPosition(WlSubsurfaceResource resource, in WlSubsurfaceResource.SetPositionEventArgs args)
    {
        _pendingX = args.X;
        _pendingY = args.Y;
        _hasPendingPosition = true;
    }

    void IWlSubsurfaceHandler.PlaceAbove(WlSubsurfaceResource resource, in WlSubsurfaceResource.PlaceAboveEventArgs args) =>
        Restack(args.Sibling, above: true);

    void IWlSubsurfaceHandler.PlaceBelow(WlSubsurfaceResource resource, in WlSubsurfaceResource.PlaceBelowEventArgs args) =>
        Restack(args.Sibling, above: false);

    void IWlSubsurfaceHandler.SetSync(WlSubsurfaceResource resource) => Synchronized = true;

    void IWlSubsurfaceHandler.SetDesync(WlSubsurfaceResource resource)
    {
        Synchronized = false;
        if (!IsEffectivelySynchronized)
        {
            Surface.ApplyCacheOnDesync();
        }
    }

    /// <summary>Gets the subsurface resource.</summary>
    public WlSubsurfaceResource Resource { get; }

    /// <summary>Gets the child surface.</summary>
    public Surface Surface { get; }

    /// <summary>Gets the parent surface.</summary>
    public Surface Parent { get; }

    /// <summary>Gets the horizontal position relative to the parent, in the parent's logical coordinates, as of the parent's last commit.</summary>
    public int X { get; private set; }

    /// <summary>Gets the vertical position relative to the parent, in the parent's logical coordinates, as of the parent's last commit.</summary>
    public int Y { get; private set; }

    /// <summary>Gets a value indicating whether the client set synchronized mode. A new subsurface starts synchronized.</summary>
    public bool Synchronized { get; private set; } = true;

    /// <summary>Gets a value indicating whether the subsurface or any ancestor subsurface is synchronized.</summary>
    public bool IsEffectivelySynchronized =>
        Synchronized || Parent.SubsurfaceRole?.IsEffectivelySynchronized == true;

    internal void ApplyPendingPlacement()
    {
        if (_hasPendingPosition)
        {
            X = ServerScaling.ToLogical(_pendingX, Parent.ServerScale);
            Y = ServerScaling.ToLogical(_pendingY, Parent.ServerScale);
            _hasPendingPosition = false;
        }

        if (_pendingRestack is { } restack)
        {
            _pendingRestack = null;
            ApplyRestack(restack.Anchor, restack.Above);
        }
    }

    internal void OnParentCommitted()
    {
        Surface.ApplyCachedState();
    }

    internal void OnSurfaceDestroyed() => OnRoleDestroyed();

    private (Surface? Anchor, bool Above)? _pendingRestack;

    private void Restack(WlSurfaceResource? siblingResource, bool above)
    {
        Surface? anchor;
        if (siblingResource == Parent.Resource)
        {
            anchor = null;
        }
        else
        {
            var sibling = FindSibling(siblingResource);
            if (sibling is null)
            {
                Resource.PostError(
                    (uint)WlSubsurface.Error.BadSurface,
                    "place_above/place_below reference is not a sibling or the parent");
                return;
            }

            anchor = sibling.Surface;
        }

        _pendingRestack = (anchor, above);
    }

    private Subsurface? FindSibling(WlSurfaceResource? resource)
    {
        if (resource is null)
        {
            return null;
        }

        foreach (var candidate in Parent.SubsurfacesBelow)
        {
            if (candidate != this && candidate.Surface.Resource == resource)
            {
                return candidate;
            }
        }

        foreach (var candidate in Parent.SubsurfacesAbove)
        {
            if (candidate != this && candidate.Surface.Resource == resource)
            {
                return candidate;
            }
        }

        return null;
    }

    private void ApplyRestack(Surface? anchorSurface, bool above)
    {
        if (anchorSurface is null)
        {
            Parent.SubsurfacesBelow.Remove(this);
            Parent.SubsurfacesAbove.Remove(this);
            if (above)
            {
                Parent.SubsurfacesAbove.Insert(0, this);
            }
            else
            {
                Parent.SubsurfacesBelow.Add(this);
            }

            return;
        }

        var role = anchorSurface.SubsurfaceRole;
        if (role is null || role == this || role.Parent != Parent
            || (!Parent.SubsurfacesBelow.Contains(role) && !Parent.SubsurfacesAbove.Contains(role)))
        {
            return;
        }

        Parent.SubsurfacesBelow.Remove(this);
        Parent.SubsurfacesAbove.Remove(this);

        var below = Parent.SubsurfacesBelow.IndexOf(role);
        if (below >= 0)
        {
            Parent.SubsurfacesBelow.Insert(above ? below + 1 : below, this);
            return;
        }

        var aboveIndex = Parent.SubsurfacesAbove.IndexOf(role);
        Parent.SubsurfacesAbove.Insert(above ? aboveIndex + 1 : aboveIndex, this);
    }

    private void OnRoleDestroyed()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        Parent.SubsurfacesBelow.Remove(this);
        Parent.SubsurfacesAbove.Remove(this);
        Surface.ClearRoleObject();
        BasinCounters.Untrack();
    }
}
