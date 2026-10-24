namespace Basin.Portal;

/// <summary>
/// A session kind that can carry a screen cast. ScreenCast and RemoteDesktop sessions implement it.
/// </summary>
public interface IScreenCastCarrier
{
    /// <summary>
    /// Gets the screen cast state of the session.
    /// </summary>
    ScreenCastState ScreenCast { get; }

    /// <summary>
    /// Gets the session that carries the screen cast.
    /// </summary>
    PortalSession Session { get; }
}
