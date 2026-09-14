using Tmds.DBus.Protocol;

namespace Basin.Portal;

public sealed class ScreenCastSession : PortalSession, IScreenCastCarrier
{
    private readonly PortalScreenCastModule _owner;

    internal ScreenCastSession(PortalScreenCastModule owner, PortalBus bus, ObjectPath handle, string appId)
        : base(bus, handle, appId) => _owner = owner;

    public ScreenCastState ScreenCast { get; } = new();

    public PortalSession Session => this;

    protected override void CloseCore() => _owner.ReleaseStreams(ScreenCast);
}
