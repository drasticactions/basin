using Tmds.DBus.Protocol;

namespace Basin.Portal;

/// <summary>
/// A ScreenCast portal session. Its close releases every stream that it published.
/// </summary>
public sealed class ScreenCastSession : PortalSession, IScreenCastCarrier
{
    private readonly PortalScreenCastModule _owner;

    internal ScreenCastSession(PortalScreenCastModule owner, PortalBus bus, ObjectPath handle, string appId)
        : base(bus, handle, appId) => _owner = owner;

    /// <inheritdoc/>
    public ScreenCastState ScreenCast { get; } = new();

    /// <inheritdoc/>
    public PortalSession Session => this;

    /// <inheritdoc/>
    protected override void CloseCore() => _owner.ReleaseStreams(ScreenCast);
}
