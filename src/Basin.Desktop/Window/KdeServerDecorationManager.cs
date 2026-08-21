using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class KdeServerDecorationManager : IDisposable
{
    public const int Version = 1;

    public enum DecorationMode : uint
    {
        None = 0,
        Client = 1,
        Server = 2,
    }

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly DecorationMode _defaultMode;
    private readonly Dictionary<Surface, DecorationMode> _modes = [];

    public KdeServerDecorationManager(WlServerDisplay display, CompositorGlobal compositor, DecorationMode defaultMode = DecorationMode.Server)
    {
        _compositor = compositor;
        _defaultMode = defaultMode;
        _global = display.CreateGlobal(OrgKdeKwinServerDecorationManager.Interface, Version, OnBind);
    }

    public event Action<Surface, DecorationMode>? ModeRequested;

    public void Dispose() => _global.Dispose();

    public DecorationMode ModeOf(Surface surface) => _modes.GetValueOrDefault(surface, DecorationMode.Client);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinServerDecorationManagerResource(client, version, id);
        manager.SendDefaultMode((uint)_defaultMode);
        manager.Create += (_, e) =>
        {
            var decoration = new OrgKdeKwinServerDecorationResource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            decoration.SendMode((uint)_defaultMode);
            if (surface is not null)
            {
                _modes[surface] = _defaultMode;
                surface.Destroyed += () => _modes.Remove(surface);
            }

            decoration.RequestMode += (_, me) =>
            {
                decoration.SendMode((uint)me.Mode);
                if (surface is not null)
                {
                    _modes[surface] = (DecorationMode)me.Mode;
                    ModeRequested?.Invoke(surface, (DecorationMode)me.Mode);
                }
            };
            decoration.Destroyed += (_, _) =>
            {
                if (surface is not null)
                {
                    _modes.Remove(surface);
                }
            };
        };
    }
}
