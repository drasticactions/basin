using Basin.Plasma.Protocol;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class PlasmaShellManager : Basin.Capabilities.IToplevelObserver, IDisposable
{
    public const int Version = 8;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly XdgToplevelSource? _toplevels;
    private readonly Dictionary<Surface, PlasmaShellSurface> _surfaces = [];
    private readonly List<PlasmaShellSurface> _list = [];

    public PlasmaShellManager(
        WlServerDisplay display, CompositorGlobal compositor, XdgToplevelSource? toplevels = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _toplevels = toplevels;
        _global = display.CreateGlobal(OrgKdePlasmaShell.Interface, Version, OnBind);
        _toplevels?.AddObserver(this);
    }

    public IReadOnlyList<PlasmaShellSurface> Surfaces => _list;

    public WlClient? LastBinder { get; private set; }

    public event Action<PlasmaShellSurface>? SurfaceAdded;

    public PlasmaShellSurface? For(Surface? surface) =>
        surface is null ? null : _surfaces.GetValueOrDefault(surface);

    public void OnToplevelAdded(ulong toplevelId)
    {
        if (_toplevels?.WindowFor(toplevelId) is { } window &&
            _surfaces.TryGetValue(window.Surface, out var shellSurface))
        {
            ForwardSkip(shellSurface);
        }
    }

    public void OnToplevelChanged(ulong toplevelId)
    {
    }

    public void OnToplevelRemoved(ulong toplevelId)
    {
    }

    public void Dispose()
    {
        _toplevels?.RemoveObserver(this);
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new OrgKdePlasmaShellResource(client, version, id);
        LastBinder = client;
        resource.GetSurface += (_, e) =>
        {
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                resource.PostError(0, "unknown wl_surface");
                return;
            }

            if (_surfaces.ContainsKey(surface))
            {
                resource.PostError(0, "the wl_surface already has a plasma surface");
                return;
            }

            var shellResource = new OrgKdePlasmaSurfaceResource(client, resource.Version, e.Id);
            var shellSurface = new PlasmaShellSurface(surface, shellResource);
            _surfaces[surface] = shellSurface;
            _list.Add(shellSurface);
            shellSurface.Destroyed += () =>
            {
                _surfaces.Remove(surface);
                _list.Remove(shellSurface);
            };
            if (_toplevels is not null)
            {
                shellSurface.SkipChanged += () => ForwardSkip(shellSurface);
                ForwardSkip(shellSurface);
            }

            SurfaceAdded?.Invoke(shellSurface);
        };
    }

    private void ForwardSkip(PlasmaShellSurface shellSurface)
    {
        if (_toplevels is { } toplevels &&
            shellSurface.Surface.RoleObject is XdgToplevelWindow window)
        {
            toplevels.SetSkipTaskbar(window, shellSurface.SkipTaskbar);
            toplevels.SetSkipSwitcher(window, shellSurface.SkipSwitcher);
        }
    }
}
