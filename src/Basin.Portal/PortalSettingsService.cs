using Basin.Capabilities;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// Serves only org.freedesktop.impl.portal.Settings from the compositor, on its own bus name, from the compositor's <see cref="IAppearanceSettings"/>.
/// The portal process serves every other interface, but only the compositor holds the appearance preferences, so the compositor serves this one.
/// </summary>
public sealed class PortalSettingsService : IDisposable
{
    /// <summary>
    /// The bus name that the service owns. The <c>basin-settings.portal</c> file names it.
    /// </summary>
    public const string BusName = "org.freedesktop.impl.portal.desktop.basin.settings";

    private readonly PortalBus _bus;
    private readonly BasinServices _services;
    private bool _disposed;

    private PortalSettingsService(PortalBus bus, BasinServices services)
    {
        _bus = bus;
        _services = services;
    }

    /// <summary>
    /// Gets a task that completes when the service owns <see cref="BusName"/>, or faults when it cannot.
    /// </summary>
    public Task Started => _bus.Started;

    /// <summary>
    /// Starts the service. Call it on the loop thread.
    /// </summary>
    /// <param name="loop">The compositor loop.</param>
    /// <param name="appearance">The appearance preferences to serve.</param>
    /// <param name="address">The bus address, or <see langword="null"/> for the session bus.</param>
    /// <param name="busName">The bus name, or <see langword="null"/> for <see cref="BusName"/>.</param>
    /// <param name="reason">The reason when the method returns <see langword="null"/>, otherwise <see langword="null"/>.</param>
    /// <returns>The service, which must be disposed, or <see langword="null"/> when there is no bus.</returns>
    public static PortalSettingsService? TryStart(ICompositorEventLoop loop, IAppearanceSettings appearance, string? address, string? busName, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(appearance);
        if (address is null && !PortalBus.HasSessionBus)
        {
            reason = "this session has no D-Bus session bus";
            Log.Info($"settings: not served, {reason}");
            return null;
        }

        var bus = new PortalBus(loop, address, busName ?? BusName);
        var pack = PortalPack.Default;
        foreach (var name in PortalRegistration.AllInterfaces)
        {
            if (name != "org.freedesktop.impl.portal.Settings")
            {
                pack = pack.Without(name);
            }
        }

        var services = new BasinServices(loop)
            .Use(bus)
            .Use(appearance)
            .Install(pack)
            .Freeze();
        var service = new PortalSettingsService(bus, services);
        _ = service.LogStartAsync();
        reason = null;
        return service;
    }

    /// <summary>
    /// Releases the bus name and closes the connection. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.Dispose();
        _bus.Dispose();
    }

    private async Task LogStartAsync()
    {
        try
        {
            await _bus.Started.ConfigureAwait(false);
            Log.Info($"settings: served on {_bus.BusName}");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Log.Info($"settings: not served, {error.Message}");
        }
    }
}
