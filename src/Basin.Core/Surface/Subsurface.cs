using Basin.Diagnostics;
using Wayland;

namespace Basin;

public sealed class Subsurface
{
    public const string RoleName = "wl_subsurface";

    private int _pendingX;
    private int _pendingY;
    private bool _hasPendingPosition;
    private bool _destroyed;

    internal Subsurface(WlSubsurfaceResource resource, Surface surface, Surface parent)
    {
        Resource = resource;
        Surface = surface;
        Parent = parent;
        BasinCounters.Track();

        surface.SubsurfaceRole = this;

        parent.SubsurfacesAbove.Add(this);

        resource.SetPosition += (_, e) =>
        {
            _pendingX = e.X;
            _pendingY = e.Y;
            _hasPendingPosition = true;
        };

        resource.PlaceAbove += (_, e) => Restack(e.Sibling, above: true);
        resource.PlaceBelow += (_, e) => Restack(e.Sibling, above: false);

        resource.SetSync += (_, _) => Synchronized = true;

        resource.SetDesync += (_, _) =>
        {
            Synchronized = false;
            if (!IsEffectivelySynchronized)
            {
                Surface.ApplyCacheOnDesync();
            }
        };

        resource.Destroyed += (_, _) => OnRoleDestroyed();
    }

    public WlSubsurfaceResource Resource { get; }

    public Surface Surface { get; }

    public Surface Parent { get; }

    public int X { get; private set; }

    public int Y { get; private set; }

    public bool Synchronized { get; private set; } = true;

    public bool IsEffectivelySynchronized =>
        Synchronized || Parent.SubsurfaceRole?.IsEffectivelySynchronized == true;

    internal void ApplyPendingPlacement()
    {
        if (_hasPendingPosition)
        {
            X = _pendingX;
            Y = _pendingY;
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
        Parent.SubsurfacesBelow.Remove(this);
        Parent.SubsurfacesAbove.Remove(this);

        if (anchorSurface is null)
        {
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
        if (role is null)
        {
            return;
        }

        var below = Parent.SubsurfacesBelow.IndexOf(role);
        if (below >= 0)
        {
            Parent.SubsurfacesBelow.Insert(above ? below + 1 : below, this);
            return;
        }

        var aboveIndex = Parent.SubsurfacesAbove.IndexOf(role);
        if (aboveIndex >= 0)
        {
            Parent.SubsurfacesAbove.Insert(above ? aboveIndex + 1 : aboveIndex, this);
        }
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
