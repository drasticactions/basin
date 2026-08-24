using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class LockscreenOverlayManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly LockOverlaySurfaces _allowed;

    public LockscreenOverlayManager(
        WlServerDisplay display, CompositorGlobal compositor, LockOverlaySurfaces allowed)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(allowed);
        _compositor = compositor;
        _allowed = allowed;
        _global = display.CreateGlobal(KdeLockscreenOverlayV1.Interface, Version, OnBind);
    }

    public ILockOverlaySurfaces Allowed => _allowed;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new KdeLockscreenOverlayV1Resource(client, version, id);
        resource.Allow += (_, e) =>
        {
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (surface.IsMapped)
            {
                resource.PostError(
                    (uint)KdeLockscreenOverlayV1.Error.InvalidSurfaceState,
                    "the surface must be unmapped when it is allowed");
                return;
            }

            _allowed.Allow(surface);
        };
    }
}
