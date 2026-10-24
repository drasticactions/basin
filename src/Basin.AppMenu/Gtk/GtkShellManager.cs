using Basin.AppMenu.Protocol;
using Wayland.Server;

namespace Basin.AppMenu;

/// <summary>
/// The <c>gtk_shell1</c> global. It records GMenu addresses in <see cref="AppMenuAddresses"/> and reports the other <c>gtk_surface1</c> requests as events for the compositor to act on.
/// </summary>
public sealed class GtkShellManager : IDisposable
{
    /// <summary>The version of the global that the manager advertises.</summary>
    public const int Version = 5;

    /// <summary>
    /// The capability bit that tells GTK the compositor shows a global menu bar. GTK tests the capabilities as a bit mask.
    /// </summary>
    public const uint GlobalMenuBar = 2;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly AppMenuAddresses _addresses;
    private readonly List<GtkShell1Resource> _shells = [];
    private bool _showsMenuBar;

    /// <summary>
    /// Initializes a new instance of the <see cref="GtkShellManager"/> class and advertises the global.
    /// </summary>
    /// <param name="display">The display that the global is created on.</param>
    /// <param name="compositor">The compositor global that resolves surface resources.</param>
    /// <param name="addresses">The table that receives the addresses and supplies <see cref="AppMenuAddresses.ShowsMenuBar"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public GtkShellManager(WlServerDisplay display, CompositorGlobal compositor, AppMenuAddresses addresses)
    {
        ArgumentNullException.ThrowIfNull(display);
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _addresses = addresses ?? throw new ArgumentNullException(nameof(addresses));
        _showsMenuBar = addresses.ShowsMenuBar;
        _global = display.CreateGlobal(GtkShell1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a client asks to present a surface. The arguments are the surface and the event time in milliseconds.
    /// </summary>
    public event Action<Surface, uint>? PresentRequested;

    /// <summary>
    /// Occurs when a client asks for focus on a surface. The arguments are the surface and the startup id, which can be <see langword="null"/>.
    /// </summary>
    public event Action<Surface, string?>? FocusRequested;

    /// <summary>
    /// Occurs when a client marks a surface modal or not modal. The arguments are the surface and <see langword="true"/> for modal.
    /// </summary>
    public event Action<Surface, bool>? ModalChanged;

    /// <summary>
    /// Occurs when a client reports a titlebar gesture on a surface. A gesture outside <see cref="GtkShellGesture"/> is a protocol error and raises nothing.
    /// </summary>
    public event Action<Surface, GtkShellGesture>? TitlebarGesture;

    /// <summary>
    /// Occurs when a client rings the system bell. The argument is the surface, or <see langword="null"/> when the request names none.
    /// </summary>
    public event Action<Surface?>? BellRequested;

    /// <summary>
    /// Gets the capability mask that clients receive: <see cref="GlobalMenuBar"/> when <see cref="AppMenuAddresses.ShowsMenuBar"/> is set, and 0 otherwise.
    /// </summary>
    public uint Capabilities => _addresses.ShowsMenuBar ? GlobalMenuBar : 0;

    /// <summary>
    /// Sends <see cref="Capabilities"/> to every bound client when <see cref="AppMenuAddresses.ShowsMenuBar"/> changed since the last send.
    /// </summary>
    public void RefreshCapabilities()
    {
        if (_showsMenuBar == _addresses.ShowsMenuBar)
        {
            return;
        }

        _showsMenuBar = _addresses.ShowsMenuBar;
        foreach (var shell in _shells)
        {
            shell.SendCapabilities(Capabilities);
        }
    }

    /// <summary>Removes the global.</summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var shell = new GtkShell1Resource(client, version, id, new Shell(this));
        _shells.Add(shell);
        shell.Destroyed += (_, _) => _shells.Remove(shell);
        shell.SendCapabilities(Capabilities);
    }

    private void Clear(Surface surface)
    {
        if (!surface.IsDestroyed && _addresses.AddressOf(surface)?.Kind == AppMenuAddressKind.GMenu)
        {
            _addresses.Set(surface, null);
        }
    }

    private sealed class Shell : IGtkShell1Handler
    {
        private readonly GtkShellManager _owner;
        private readonly Dictionary<GtkSurface1Resource, Surface> _surfaces = [];

        internal Shell(GtkShellManager owner)
        {
            _owner = owner;
        }

        void IGtkShell1Handler.GetGtkSurface(GtkShell1Resource resource, in GtkShell1Resource.GetGtkSurfaceEventArgs args)
        {
            if (_owner._compositor.ResolveSurface(args.Surface) is not { } surface)
            {
                _ = new GtkSurface1Resource(resource.Client, resource.Version, args.GtkSurface);
                return;
            }

            var gtk = new GtkSurface1Resource(resource.Client, resource.Version, args.GtkSurface, new GtkSurface(_owner, surface));
            _surfaces[gtk] = surface;
            gtk.Destroyed += (_, _) =>
            {
                _owner.Clear(surface);
                _surfaces.Remove(gtk);
            };
        }

        void IGtkShell1Handler.SetStartupId(GtkShell1Resource resource, in GtkShell1Resource.SetStartupIdEventArgs args)
        {
        }

        void IGtkShell1Handler.SystemBell(GtkShell1Resource resource, in GtkShell1Resource.SystemBellEventArgs args) =>
            _owner.BellRequested?.Invoke(args.Surface is GtkSurface1Resource gtk && _surfaces.TryGetValue(gtk, out var surface) ? surface : null);

        void IGtkShell1Handler.NotifyLaunch(GtkShell1Resource resource, in GtkShell1Resource.NotifyLaunchEventArgs args)
        {
        }
    }

    private sealed class GtkSurface : IGtkSurface1Handler
    {
        private readonly GtkShellManager _owner;
        private readonly Surface _surface;

        internal GtkSurface(GtkShellManager owner, Surface surface)
        {
            _owner = owner;
            _surface = surface;
        }

        void IGtkSurface1Handler.SetDbusProperties(GtkSurface1Resource resource, in GtkSurface1Resource.SetDbusPropertiesEventArgs args)
        {
            if (_surface.IsDestroyed)
            {
                return;
            }

            if (args.UniqueBusName is not { Length: > 0 } bus || (args.MenubarPath is not { Length: > 0 } && args.AppMenuPath is not { Length: > 0 }))
            {
                _owner.Clear(_surface);
                return;
            }

            _owner._addresses.Set(_surface, new AppMenuAddress(
                AppMenuAddressKind.GMenu,
                bus,
                MenubarPath: args.MenubarPath ?? "",
                AppMenuPath: args.AppMenuPath ?? "",
                ApplicationPath: args.ApplicationObjectPath ?? "",
                WindowPath: args.WindowObjectPath ?? ""));
        }

        void IGtkSurface1Handler.SetModal(GtkSurface1Resource resource) => _owner.ModalChanged?.Invoke(_surface, true);

        void IGtkSurface1Handler.UnsetModal(GtkSurface1Resource resource) => _owner.ModalChanged?.Invoke(_surface, false);

        void IGtkSurface1Handler.Present(GtkSurface1Resource resource, in GtkSurface1Resource.PresentEventArgs args) =>
            _owner.PresentRequested?.Invoke(_surface, args.Time);

        void IGtkSurface1Handler.RequestFocus(GtkSurface1Resource resource, in GtkSurface1Resource.RequestFocusEventArgs args) =>
            _owner.FocusRequested?.Invoke(_surface, args.StartupId);

        void IGtkSurface1Handler.Release(GtkSurface1Resource resource)
        {
        }

        void IGtkSurface1Handler.TitlebarGesture(GtkSurface1Resource resource, in GtkSurface1Resource.TitlebarGestureEventArgs args)
        {
            if ((uint)args.Gesture is < 1 or > 3)
            {
                resource.PostError((uint)GtkSurface1.Error.InvalidGesture, "The titlebar gesture is not known.");
                return;
            }

            _owner.TitlebarGesture?.Invoke(_surface, (GtkShellGesture)(uint)args.Gesture);
        }
    }
}
