using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class AlphaModifierManager : IDisposable
{
    public const int Version = 1;

    private const uint ErrorAlreadyConstructed = 0;
    private const uint ErrorNoSurface = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly ISurfaceAppearance _appearance;
    private readonly HashSet<Surface> _claimed = [];

    public AlphaModifierManager(WlServerDisplay display, CompositorGlobal compositor, ISurfaceAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(appearance);
        _compositor = compositor;
        _appearance = appearance;
        _global = display.CreateGlobal(WpAlphaModifierV1.Interface, Version, OnBind);
    }

    public event Action<Surface, double>? AlphaChanged;

    public ISurfaceAppearance Appearance => _appearance;

    public void Dispose() => _global.Dispose();

    public double AlphaOf(Surface surface) => _appearance.OpacityOf(surface);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpAlphaModifierV1Resource(client, version, id);
        manager.GetSurface += (_, e) =>
        {
            var resource = new WpAlphaModifierSurfaceV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_claimed.Add(surface))
            {
                manager.PostError(ErrorAlreadyConstructed, "surface already has an alpha modifier");
                return;
            }

            var alive = true;
            Action committed = () =>
            {
                if (surface.Current.TakeExtension<PendingAlpha>() is { } pending)
                {
                    _appearance.SetOpacity(surface, pending.Alpha);
                    AlphaChanged?.Invoke(surface, pending.Alpha);
                }
            };
            surface.Committed += committed;

            resource.SetMultiplier += (_, me) =>
            {
                if (!alive)
                {
                    resource.PostError(ErrorNoSurface, "the wl_surface has been destroyed");
                    return;
                }

                surface.Pending.SetExtension(new PendingAlpha(me.Factor / (double)uint.MaxValue));
            };
            resource.Destroyed += (_, _) =>
            {
                _claimed.Remove(surface);
                if (alive)
                {
                    surface.Pending.SetExtension(new PendingAlpha(1.0));
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

    private sealed class PendingAlpha(double alpha) : IDisposable
    {
        public double Alpha { get; } = alpha;

        public void Dispose()
        {
        }
    }
}
