using Basin.Capabilities;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public abstract class PortalModule : IProtocolModule, IDisposable
{
    private PortalBus? _bus;
    private IAppInfoResolver? _appInfo;

    public abstract string WireInterface { get; }

    public abstract int Version { get; }

    public virtual IReadOnlyList<Type> Capabilities => [];

    public virtual IReadOnlyList<Type> Drivers => [];

    internal abstract DBusHandler.DBusInterface Interface { get; }

    public PortalBus? Bus => _bus;

    public bool IsInstalled => _bus is not null;

    public virtual void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<PortalBus>() is null && PortalBus.HasSessionBus)
        {
            services.UseDefault(new PortalBus(services.Loop, ownedByModules: true));
        }
    }

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

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _bus = services.Require<PortalBus>();
        _appInfo = services.Find<IAppInfoResolver>();
        Bind(services);
        _bus.Attach(this);
        return this;
    }

    protected abstract void Bind(BasinServices services);

    protected virtual void Unbind()
    {
    }

    protected AppInfo Identify(string appId)
    {
        if (!string.IsNullOrEmpty(appId) && _appInfo is not null && _appInfo.TryResolve(appId, out var info))
        {
            return new AppInfo(info.DisplayName ?? "", info.IconPath ?? "");
        }

        return new AppInfo("", "");
    }

    protected string Describe(string appId)
    {
        var info = Identify(appId);
        if (info.DisplayName.Length > 0)
        {
            return info.DisplayName;
        }

        return string.IsNullOrEmpty(appId) ? "An unknown application" : appId;
    }

    protected SourcePrompt Named(in SourcePrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    protected DevicePrompt Named(in DevicePrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    protected ConfirmPrompt Named(in ConfirmPrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    protected AreaPrompt Named(in AreaPrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    protected ShortcutPrompt Named(in ShortcutPrompt prompt)
    {
        var info = Identify(prompt.AppId);
        return prompt with { DisplayName = info.DisplayName, IconPath = info.IconPath };
    }

    protected PortalCall Call => _bus?.Current ?? default;

    protected DBusConnection Connection =>
        _bus?.Connection ?? throw new InvalidOperationException("the portal bus is not connected");

    protected PortalRequest TrackRequest(ObjectPath handle) =>
        _bus?.Track(handle) ?? throw new InvalidOperationException("the portal bus is not connected");

    protected void ReleaseRequest(PortalRequest request) => _bus?.Release(request);

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
