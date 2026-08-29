using Basin.Diagnostics;
using Pixman;
using Wayland;

namespace Basin;

public static class SurfaceCommit
{
    public static void Move(SurfaceState pending, SurfaceState target, bool targetIsCurrent = false)
    {
        var fields = pending.Committed;

        if ((fields & SurfaceStateFields.Buffer) != 0)
        {
            target.Buffer?.Unlock();
            target.BufferRelease?.Done(0);
            target.BufferRelease = null;
            target.Buffer = pending.Buffer;
            pending.Buffer = null;
        }

        if ((fields & SurfaceStateFields.BufferRelease) != 0)
        {
            target.BufferRelease?.Cancel();
            target.BufferRelease = pending.BufferRelease;
            pending.BufferRelease = null;
        }

        if ((fields & SurfaceStateFields.Offset) != 0)
        {
            target.OffsetX = pending.OffsetX;
            target.OffsetY = pending.OffsetY;
        }

        if ((fields & SurfaceStateFields.SurfaceDamage) != 0)
        {
            if (targetIsCurrent)
            {
                target.SurfaceDamage.Copy(pending.SurfaceDamage);
                target.SurfaceDamageRects = pending.SurfaceDamageRects;
            }
            else
            {
                target.SurfaceDamage.UnionWith(pending.SurfaceDamage);
                target.SurfaceDamageRects.Add(in pending.SurfaceDamageRects);
            }

            pending.SurfaceDamage.Clear();
            pending.SurfaceDamageRects.Clear();
        }
        else if (targetIsCurrent)
        {
            target.SurfaceDamage.Clear();
            target.SurfaceDamageRects.Clear();
        }

        if ((fields & SurfaceStateFields.BufferDamage) != 0)
        {
            if (targetIsCurrent)
            {
                target.BufferDamage.Copy(pending.BufferDamage);
                target.BufferDamageRects = pending.BufferDamageRects;
            }
            else
            {
                target.BufferDamage.UnionWith(pending.BufferDamage);
                target.BufferDamageRects.Add(in pending.BufferDamageRects);
            }

            pending.BufferDamage.Clear();
            pending.BufferDamageRects.Clear();
        }
        else if (targetIsCurrent)
        {
            target.BufferDamage.Clear();
            target.BufferDamageRects.Clear();
        }

        if ((fields & SurfaceStateFields.OpaqueRegion) != 0 && (pending.HasOpaque || target.HasOpaque))
        {
            target.Opaque.Copy(pending.Opaque);
        }

        if ((fields & SurfaceStateFields.InputRegion) != 0)
        {
            if (pending.HasInput || target.HasInput)
            {
                target.Input.Copy(pending.Input);
            }

            target.InputIsInfinite = pending.InputIsInfinite;
        }

        if ((fields & SurfaceStateFields.Transform) != 0)
        {
            target.Transform = pending.Transform;
        }

        if ((fields & SurfaceStateFields.Scale) != 0)
        {
            target.Scale = pending.Scale;
        }

        if ((fields & SurfaceStateFields.Viewport) != 0)
        {
            target.ViewportSourceX = pending.ViewportSourceX;
            target.ViewportSourceY = pending.ViewportSourceY;
            target.ViewportSourceWidth = pending.ViewportSourceWidth;
            target.ViewportSourceHeight = pending.ViewportSourceHeight;
            target.ViewportDestinationWidth = pending.ViewportDestinationWidth;
            target.ViewportDestinationHeight = pending.ViewportDestinationHeight;
        }

        if ((fields & SurfaceStateFields.FrameCallbacks) != 0)
        {
            target.FrameCallbacks.AddRange(pending.FrameCallbacks);
            pending.FrameCallbacks.Clear();
            target.FrameResources.AddRange(pending.FrameResources);
            pending.FrameResources.Clear();
        }

        pending.MoveExtensionsTo(target);

        target.Committed |= fields;
        pending.Committed = SurfaceStateFields.None;
    }
}
