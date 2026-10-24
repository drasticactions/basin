using Basin.Capabilities;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// The base of a module that serves one portal interface on a shared <see cref="PortalBus"/>. It installs through <see cref="BasinServices"/> like a Wayland global.
/// </summary>
public abstract class PortalModule : IProtocolModule, IDisposable
{
    private PortalBus? _bus;
    private IAppInfoResolver? _appInfo;

    /// <inheritdoc/>
    public abstract string WireInterface { get; }

    /// <inheritdoc/>
    public abstract int Version { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<Type> Capabilities => [];

    /// <inheritdoc/>
    public virtual IReadOnlyList<Type> Drivers => [];

    internal abstract DBusHandler.DBusInterface Interface { get; }

    /// <summary>
    /// Gets the bus that the module is installed on. It is <see langword="null"/> before install and after dispose.
    /// </summary>
    public PortalBus? Bus => _bus;

    /// <summary>
    /// Gets a value indicating whether the module is installed on a bus.
    /// </summary>
    public bool IsInstalled => _bus is not null;

    /// <summary>
    /// Registers a default <see cref="PortalBus"/> that the modules own, when none is registered and the process has a session bus.
    /// </summary>
    /// <param name="services">The registry to seed.</param>
    public virtual void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<PortalBus>() is null && PortalBus.HasSessionBus)
        {
            services.UseDefault(new PortalBus(services.Loop, ownedByModules: true));
        }
    }

    /// <summary>
    /// Decides whether the interface is offered. The default offers it only when a <see cref="PortalBus"/> is registered.
    /// </summary>
    /// <param name="services">The registry after all defaults are seeded.</param>
    /// <returns><see langword="false"/> when no bus is registered.</returns>
    public virtual bool ShouldInstall(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<PortalBus>() is not null)
        {
            return true;
        }

        Log.Info($"{WireInterface} not offered: this session has no D-Bus session bus (DBUS_SESSION_BUS_ADDRESS is unset)");
        return false;
    }

    /// <summary>
    /// Binds the module to the registered <see cref="PortalBus"/> and its services, then attaches it, which starts the bus.
    /// </summary>
    /// <param name="services">The registry that holds the bus and the module's capabilities.</param>
    /// <returns>The module itself. Disposing it detaches the module.</returns>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _bus = services.Require<PortalBus>();
        _appInfo = services.Find<IAppInfoResolver>();
        Bind(services);
        _bus.Attach(this);
        return this;
    }

    /// <summary>
    /// Reads the capabilities that the module uses. It runs once, during <see cref="Install"/>, before the module receives calls.
    /// </summary>
    /// <param name="services">The registry to read from.</param>
    protected abstract void Bind(BasinServices services);

    /// <summary>
    /// Releases what <see cref="Bind"/> took. It runs once, during <see cref="Dispose"/>. The default does nothing.
    /// </summary>
    protected virtual void Unbind()
    {
    }

    /// <summary>
    /// Looks up the display name and icon of an application through the registered <see cref="IAppInfoResolver"/>.
    /// </summary>
    /// <param name="appId">The application id from the frontend.</param>
    /// <returns>The name and icon path, which are empty strings when the id is empty or unknown.</returns>
    protected AppInfo Identify(string appId)
    {
        if (!string.IsNullOrEmpty(appId) && _appInfo is not null && _appInfo.TryResolve(appId, out var info))
        {
            return new AppInfo(info.DisplayName ?? "", info.IconPath ?? "");
        }

        return new AppInfo("", "");
    }

    /// <summary>
    /// Gets a name for an application that a prompt can show.
    /// </summary>
    /// <param name="appId">The application id from the frontend.</param>
    /// <returns>The display name, else the id, else "An unknown application".</returns>
    protected string Describe(string appId)
    {
        var info = Identify(appId);
        if (info.DisplayName.Length > 0)
        {
            return info.DisplayName;
        }

        return string.IsNullOrEmpty(appId) ? "An unknown application" : appId;
    }

    /// <summary>
    /// Fills the display name and icon of a prompt from its application id.
    /// </summary>
    /// <param name="prompt">The prompt to copy.</param>
    /// <returns>A copy of <paramref name="prompt"/> with the name and icon set.</returns>
    protected SourcePrompt Named(in SourcePrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    /// <summary>
    /// Fills the display name and icon of a prompt from its application id.
    /// </summary>
    /// <param name="prompt">The prompt to copy.</param>
    /// <returns>A copy of <paramref name="prompt"/> with the name and icon set.</returns>
    protected DevicePrompt Named(in DevicePrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    /// <summary>
    /// Fills the display name and icon of a prompt from its application id.
    /// </summary>
    /// <param name="prompt">The prompt to copy.</param>
    /// <returns>A copy of <paramref name="prompt"/> with the name and icon set.</returns>
    protected ConfirmPrompt Named(in ConfirmPrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    /// <summary>
    /// Fills the display name and icon of a prompt from its application id.
    /// </summary>
    /// <param name="prompt">The prompt to copy.</param>
    /// <returns>A copy of <paramref name="prompt"/> with the name and icon set.</returns>
    protected AreaPrompt Named(in AreaPrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    /// <summary>
    /// Fills the display name and icon of a prompt from its application id.
    /// </summary>
    /// <param name="prompt">The prompt to copy.</param>
    /// <returns>A copy of <paramref name="prompt"/> with the name and icon set.</returns>
    protected ShortcutPrompt Named(in ShortcutPrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    /// <summary>
    /// Gets the sender and path of the call that runs now. Read it before the first <see langword="await"/> of a handler, because the next call replaces it.
    /// </summary>
    protected PortalCall Call => _bus?.Current ?? default;

    /// <summary>
    /// Gets the bus connection for signals and replies.
    /// </summary>
    /// <exception cref="InvalidOperationException">The module is not installed or the bus is not connected.</exception>
    protected DBusConnection Connection =>
        _bus?.Connection ?? throw new InvalidOperationException("the portal bus is not connected");

    /// <summary>
    /// Registers a request object on the bus. The caller must give it back to <see cref="ReleaseRequest"/> when the method replies.
    /// </summary>
    /// <param name="handle">The request object path that the frontend gave.</param>
    /// <returns>The request whose token the frontend can cancel.</returns>
    /// <exception cref="InvalidOperationException">The module is not installed.</exception>
    protected PortalRequest TrackRequest(ObjectPath handle) =>
        _bus?.Track(handle) ?? throw new InvalidOperationException("the portal bus is not connected");

    /// <summary>
    /// Removes and disposes a request from <see cref="TrackRequest"/>. It does nothing when the module is not installed.
    /// </summary>
    /// <param name="request">The request to release.</param>
    protected void ReleaseRequest(PortalRequest request) => _bus?.Release(request);

    /// <summary>
    /// Unbinds the module and detaches it from its bus. A bus that the modules own is disposed with its last module.
    /// </summary>
    public void Dispose()
    {
        if (_bus is not { } bus)
        {
            return;
        }

        _bus = null;
        _appInfo = null;
        Unbind();
        bus.Detach(this);
    }
}
