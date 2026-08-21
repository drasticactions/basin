using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class TearingControlManager : IDisposable
{
    public const int Version = 1;

    private const uint ErrorExists = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, uint> _hints = [];
    private readonly HashSet<Surface> _claimed = [];

    public TearingControlManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpTearingControlManagerV1.Interface, Version, OnBind);
    }

    public event Action<Surface, bool>? HintChanged;

    public void Dispose() => _global.Dispose();

    public bool PrefersTearing(Surface? surface) =>
        surface is not null && _hints.GetValueOrDefault(surface) == 1;

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpTearingControlManagerV1Resource(client, version, id);
        manager.GetTearingControl += (_, e) =>
        {
            var resource = new WpTearingControlV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_claimed.Add(surface))
            {
                manager.PostError(ErrorExists, "surface already has a tearing control");
                return;
            }

            resource.SetPresentationHint += (_, he) =>
            {
                _hints[surface] = (uint)he.Hint;
                HintChanged?.Invoke(surface, (uint)he.Hint == 1);
            };
            resource.Destroyed += (_, _) =>
            {
                _claimed.Remove(surface);
                if (_hints.Remove(surface))
                {
                    HintChanged?.Invoke(surface, false);
                }
            };
            surface.Destroyed += () =>
            {
                _claimed.Remove(surface);
                _hints.Remove(surface);
            };
        };
    }
}
