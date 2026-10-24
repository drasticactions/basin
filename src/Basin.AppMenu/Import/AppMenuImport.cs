using Basin.Shell.Xdg;

namespace Basin.AppMenu;

/// <summary>
/// Holds the imported menus of one window. It starts a reader for each new address and reports when the menu is ready, failed or cleared.
/// </summary>
public sealed class AppMenuImport : IDisposable
{
    private readonly AppMenuBus _bus;
    private readonly XdgToplevelSource? _mirror;
    private readonly XdgToplevelWindow? _xdg;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppMenuImport"/> class.
    /// With <paramref name="mirror"/> and <paramref name="xdg"/>, each dbusmenu address is also set on the toplevel for foreign-toplevel observers.
    /// </summary>
    /// <param name="bus">The shared bus connection for the readers.</param>
    /// <param name="mirror">The toplevel source that receives dbusmenu addresses, or <see langword="null"/>.</param>
    /// <param name="xdg">The window's toplevel, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bus"/> is <see langword="null"/>.</exception>
    public AppMenuImport(AppMenuBus bus, XdgToplevelSource? mirror = null, XdgToplevelWindow? xdg = null)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
        _mirror = mirror;
        _xdg = xdg;
    }

    /// <summary>
    /// Occurs once for each reader, when it first holds a complete menu. The argument is the reader.
    /// </summary>
    public event Action<IAppMenuSource>? Ready;

    /// <summary>
    /// Occurs when the current reader cannot read its first menu. The argument is the address. The import drops the reader and keeps the address.
    /// </summary>
    public event Action<AppMenuAddress>? Failed;

    /// <summary>
    /// Occurs when <see cref="Import"/> changes the address to <see langword="null"/>.
    /// </summary>
    public event Action? Cleared;

    /// <summary>
    /// Gets the address that the last <see cref="Import"/> took, or <see langword="null"/>.
    /// </summary>
    public AppMenuAddress? Address { get; private set; }

    /// <summary>
    /// Gets the reader for <see cref="Address"/>. It is <see langword="null"/> when there is no address or after the reader failed.
    /// </summary>
    public IAppMenuSource? Source { get; private set; }

    /// <summary>
    /// Finds a window's menu address. The first hit wins: the surface's entry in <paramref name="addresses"/>, the X11 property pair, then the registrar's entry for the X11 window.
    /// </summary>
    /// <param name="addresses">The table that the appmenu protocols fill.</param>
    /// <param name="surface">The window's root surface, or <see langword="null"/>.</param>
    /// <param name="x11ServiceName">The X11 <c>_KDE_NET_WM_APPMENU_SERVICE_NAME</c> value, or <see langword="null"/>.</param>
    /// <param name="x11ObjectPath">The X11 <c>_KDE_NET_WM_APPMENU_OBJECT_PATH</c> value, or <see langword="null"/>.</param>
    /// <param name="registrar">The registrar to ask, or <see langword="null"/>.</param>
    /// <param name="x11WindowId">The X11 window id, or <see langword="null"/> for a Wayland window.</param>
    /// <returns>The address, or <see langword="null"/> when no step has one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="addresses"/> is <see langword="null"/>.</exception>
    public static AppMenuAddress? Resolve(
        AppMenuAddresses addresses,
        Surface? surface,
        string? x11ServiceName = null,
        string? x11ObjectPath = null,
        AppMenuRegistrar? registrar = null,
        uint? x11WindowId = null)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        if (surface is not null && addresses.AddressOf(surface) is { } address)
        {
            return address;
        }

        if (x11ServiceName is { Length: > 0 } service && x11ObjectPath is { Length: > 0 } path)
        {
            return AppMenuAddress.DbusMenu(service, path);
        }

        return x11WindowId is { } windowId ? registrar?.AddressOf(windowId) : null;
    }

    /// <summary>
    /// Switches to a new address. It disposes the previous reader and starts a <see cref="DbusMenuReader"/> or a <see cref="GMenuReader"/>, or raises <see cref="Cleared"/> for <see langword="null"/>.
    /// </summary>
    /// <param name="address">The address from <see cref="Resolve"/>.</param>
    /// <returns><see langword="false"/> when the address equals <see cref="Address"/> and nothing changed.</returns>
    public bool Import(AppMenuAddress? address)
    {
        if (Address == address)
        {
            return false;
        }

        Drop();
        Address = address;
        if (address is null)
        {
            Cleared?.Invoke();
            return true;
        }

        if (_xdg is not null && address.Kind == AppMenuAddressKind.DbusMenu)
        {
            _mirror?.SetAppMenu(_xdg, address.BusName, address.ObjectPath);
        }

        IAppMenuSource reader = address.Kind == AppMenuAddressKind.GMenu
            ? new GMenuReader(_bus, address)
            : new DbusMenuReader(_bus, address.BusName, address.ObjectPath);
        Source = reader;
        var ready = false;
        reader.Menu.Changed += () =>
        {
            if (!ready && reader.IsReady && ReferenceEquals(Source, reader))
            {
                ready = true;
                Ready?.Invoke(reader);
            }
        };
        reader.Failed += () =>
        {
            if (!ReferenceEquals(Source, reader))
            {
                return;
            }

            Source = null;
            reader.Dispose();
            Failed?.Invoke(address);
        };
        return true;
    }

    /// <summary>
    /// Disposes the reader and forgets the address. It raises no event.
    /// </summary>
    public void Dispose()
    {
        Drop();
        Address = null;
    }

    private void Drop()
    {
        Source?.Dispose();
        Source = null;
    }
}
