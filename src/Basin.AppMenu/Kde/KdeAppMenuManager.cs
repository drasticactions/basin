using Basin.AppMenu.Protocol;
using Wayland.Server;

namespace Basin.AppMenu;

/// <summary>
/// The <c>org_kde_kwin_appmenu_manager</c> global. <c>set_address</c> records a dbusmenu address for a surface, and an empty service name or path clears it.
/// Releasing the appmenu object also clears a dbusmenu address.
/// </summary>
public sealed class KdeAppMenuManager : IDisposable
{
    /// <summary>The version of the global that the manager advertises.</summary>
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly AppMenuAddresses _addresses;

    /// <summary>
    /// Initializes a new instance of the <see cref="KdeAppMenuManager"/> class and advertises the global.
    /// </summary>
    /// <param name="display">The display that the global is created on.</param>
    /// <param name="compositor">The compositor global that resolves surface resources.</param>
    /// <param name="addresses">The table that receives the addresses.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public KdeAppMenuManager(WlServerDisplay display, CompositorGlobal compositor, AppMenuAddresses addresses)
    {
        ArgumentNullException.ThrowIfNull(display);
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _addresses = addresses ?? throw new ArgumentNullException(nameof(addresses));
        _global = display.CreateGlobal(OrgKdeKwinAppmenuManager.Interface, Version, OnBind);
    }

    /// <summary>Removes the global.</summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinAppmenuManagerResource(client, version, id);
        manager.Create += (_, e) =>
        {
            var menu = new OrgKdeKwinAppmenuResource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            menu.SetAddress += (_, address) =>
            {
                if (surface.IsDestroyed)
                {
                    return;
                }

                _addresses.Set(surface, address.ServiceName.Length == 0 || address.ObjectPath.Length == 0
                    ? null
                    : AppMenuAddress.DbusMenu(address.ServiceName, address.ObjectPath));
            };
            menu.Destroyed += (_, _) =>
            {
                if (!surface.IsDestroyed && _addresses.AddressOf(surface)?.Kind == AppMenuAddressKind.DbusMenu)
                {
                    _addresses.Set(surface, null);
                }
            };
        };
    }
}
