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

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, double> _alphas = [];
    private readonly HashSet<Surface> _claimed = [];

    public AlphaModifierManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpAlphaModifierV1.Interface, Version, OnBind);
    }

    public event Action<Surface, double>? AlphaChanged;

    public void Dispose() => _global.Dispose();

    public double AlphaOf(Surface surface) => _alphas.GetValueOrDefault(surface, 1.0);

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

            resource.SetMultiplier += (_, me) =>
            {
                var alpha = me.Factor / (double)uint.MaxValue;
                _alphas[surface] = alpha;
                AlphaChanged?.Invoke(surface, alpha);
            };
            resource.Destroyed += (_, _) =>
            {
                _claimed.Remove(surface);
                if (_alphas.Remove(surface))
                {
                    AlphaChanged?.Invoke(surface, 1.0);
                }
            };
            surface.Destroyed += () =>
            {
                _claimed.Remove(surface);
                _alphas.Remove(surface);
            };
        };
    }
}
