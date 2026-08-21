using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class SystemBellManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IBell? _bell;

    public SystemBellManager(WlServerDisplay display, CompositorGlobal compositor, IBell? bell)
    {
        ArgumentNullException.ThrowIfNull(display);
        _compositor = compositor;
        _bell = bell;
        _global = display.CreateGlobal(XdgSystemBellV1.Interface, Version, OnBind);
    }

    public event Action<Surface?>? Rang;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var bell = new XdgSystemBellV1Resource(client, version, id);
        bell.Ring += (_, e) =>
        {
            var surface = e.Surface is { } resource ? _compositor.ResolveSurface(resource) : null;
            _bell?.Ring(surface);
            Rang?.Invoke(surface);
        };
    }
}
