namespace Basin.Portal;

public interface IClipboardCarrier
{
    PortalClipboard Clipboard { get; }

    PortalSession Session { get; }

    bool IsStarted { get; }
}
