namespace Basin.Portal;

public interface IScreenCastCarrier
{
    ScreenCastState ScreenCast { get; }

    PortalSession Session { get; }
}
