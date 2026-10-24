namespace Basin.Portal;

/// <summary>
/// A session kind that can carry the clipboard portal. RemoteDesktop and InputCapture sessions implement it.
/// </summary>
public interface IClipboardCarrier
{
    /// <summary>
    /// Gets the clipboard state of the session.
    /// </summary>
    PortalClipboard Clipboard { get; }

    /// <summary>
    /// Gets the session that carries the clipboard.
    /// </summary>
    PortalSession Session { get; }

    /// <summary>
    /// Gets a value indicating whether the session started. The clipboard can be requested only before the start and used only after it.
    /// </summary>
    bool IsStarted { get; }
}
