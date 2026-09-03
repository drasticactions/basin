using Basin.Capabilities;
using Basin.Hypr.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Hypr;

public sealed class HyprlandSurfaceManager : IDisposable
{
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly ISurfaceAppearance _appearance;
    private readonly HashSet<Surface> _claimed = [];

    public HyprlandSurfaceManager(WlServerDisplay display, CompositorGlobal compositor, ISurfaceAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(appearance);
        _compositor = compositor;
        _appearance = appearance;
        _global = display.CreateGlobal(HyprlandSurfaceManagerV1.Interface, Version, OnBind);
    }

    public ISurfaceAppearance Appearance => _appearance;

    public bool IsClaimed(Surface surface) => _claimed.Contains(surface);

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new HyprlandSurfaceManagerV1Resource(client, version, id);
        manager.GetHyprlandSurface += (_, e) =>
        {
            var resource = new HyprlandSurfaceV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_claimed.Add(surface))
            {
                manager.PostError(
                    (uint)HyprlandSurfaceManagerV1.Error.AlreadyConstructed,
                    "wl_surface already has a hyprland surface object");
                return;
            }

            var alive = true;
            Action committed = () =>
            {
                if (surface.Current.TakeExtension<PendingAppearance>() is { } pending)
                {
                    pending.ApplyTo(_appearance, surface);
                    pending.Dispose();
                }
            };
            surface.Committed += committed;

            resource.SetOpacity += (_, oe) =>
            {
                if (!alive)
                {
                    resource.PostError((uint)HyprlandSurfaceV1.Error.NoSurface, "set_opacity called for a destroyed wl_surface");
                    return;
                }

                var opacity = oe.Opacity.ToDouble();
                if (opacity < 0.0 || opacity > 1.0)
                {
                    resource.PostError((uint)HyprlandSurfaceV1.Error.OutOfRange, "opacity is outside the range 0.0 - 1.0");
                    return;
                }

                Stage(surface).Opacity = opacity;
            };
            resource.SetVisibleRegion += (_, re) =>
            {
                if (!alive)
                {
                    resource.PostError((uint)HyprlandSurfaceV1.Error.NoSurface, "set_visible_region called for a destroyed wl_surface");
                    return;
                }

                Stage(surface).SetRegion(_compositor.ResolveRegion(re.Region)?.Pixman);
            };
            resource.Destroyed += (_, _) =>
            {
                _claimed.Remove(surface);
                if (alive)
                {
                    var pending = Stage(surface);
                    pending.Opacity = 1.0;
                    pending.SetRegion(null);
                }
            };
            surface.Destroyed += () =>
            {
                alive = false;
                _claimed.Remove(surface);
                surface.Committed -= committed;
            };
        };
    }

    private static PendingAppearance Stage(Surface surface)
    {
        if (surface.Pending.GetExtension<PendingAppearance>() is { } pending)
        {
            return pending;
        }

        pending = new PendingAppearance();
        surface.Pending.SetExtension(pending);
        return pending;
    }

    private sealed class PendingAppearance : IDisposable
    {
        private PixmanRegion32? _region;
        private bool _regionSet;

        public double? Opacity { get; set; }

        public void SetRegion(PixmanRegion32? region)
        {
            _regionSet = true;
            if (region is null || region.IsEmpty)
            {
                _region?.Dispose();
                _region = null;
                return;
            }

            _region ??= new PixmanRegion32();
            _region.Copy(region);
        }

        public void ApplyTo(ISurfaceAppearance appearance, Surface surface)
        {
            if (Opacity is { } opacity)
            {
                appearance.SetOpacity(surface, opacity);
            }

            if (!_regionSet)
            {
                return;
            }

            if (_region is { } region)
            {
                appearance.SetVisibleRegion(surface, region);
            }
            else
            {
                appearance.ClearVisibleRegion(surface);
            }
        }

        public void Dispose()
        {
            _region?.Dispose();
            _region = null;
        }
    }
}
