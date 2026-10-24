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
        var shell = new GtkShell1Resource(client, version, id);
        _shells.Add(shell);
        shell.Destroyed += (_, _) => _shells.Remove(shell);
        shell.SendCapabilities(Capabilities);
        var surfaces = new Dictionary<GtkSurface1Resource, Surface>();
        shell.GetGtkSurface += (_, e) =>
        {
            var gtk = new GtkSurface1Resource(client, shell.Version, e.GtkSurface);
            if (_compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            surfaces[gtk] = surface;
            Wire(gtk, surface);
            gtk.Destroyed += (_, _) => surfaces.Remove(gtk);
        };
        shell.SystemBell += (_, e) =>
            BellRequested?.Invoke(e.Surface is GtkSurface1Resource gtk && surfaces.TryGetValue(gtk, out var surface) ? surface : null);
    }

    private void Wire(GtkSurface1Resource gtk, Surface surface)
    {
        gtk.SetDbusProperties += (_, e) =>
        {
            if (surface.IsDestroyed)
            {
                return;
            }

            if (e.UniqueBusName is not { Length: > 0 } bus || (e.MenubarPath is not { Length: > 0 } && e.AppMenuPath is not { Length: > 0 }))
            {
                Clear(surface);
                return;
            }

            _addresses.Set(surface, new AppMenuAddress(
                AppMenuAddressKind.GMenu,
                bus,
                MenubarPath: e.MenubarPath ?? "",
                AppMenuPath: e.AppMenuPath ?? "",
                ApplicationPath: e.ApplicationObjectPath ?? "",
                WindowPath: e.WindowObjectPath ?? ""));
        };
        gtk.SetModal += (_, _) => ModalChanged?.Invoke(surface, true);
        gtk.UnsetModal += (_, _) => ModalChanged?.Invoke(surface, false);
        gtk.Present += (_, e) => PresentRequested?.Invoke(surface, e.Time);
        gtk.RequestFocus += (_, e) => FocusRequested?.Invoke(surface, e.StartupId);
        gtk.TitlebarGesture += (_, e) =>
        {
            if ((uint)e.Gesture is < 1 or > 3)
            {
                gtk.PostError((uint)GtkSurface1.Error.InvalidGesture, "The titlebar gesture is not known.");
                return;
            }

            TitlebarGesture?.Invoke(surface, (GtkShellGesture)(uint)e.Gesture);
        };
        gtk.Destroyed += (_, _) => Clear(surface);
    }

    private void Clear(Surface surface)
    {
        if (!surface.IsDestroyed && _addresses.AddressOf(surface)?.Kind == AppMenuAddressKind.GMenu)
        {
            _addresses.Set(surface, null);
        }
    }
}
