using Basin.Capabilities;
using Basin.Eis;
using Tmds.DBus.Protocol;

namespace Basin.Portal;

/// <summary>
/// A RemoteDesktop portal session. It can also carry a screen cast and the clipboard.
/// </summary>
public sealed class RemoteDesktopSession : PortalSession, IScreenCastCarrier, IClipboardCarrier
{
    private readonly PortalRemoteDesktopModule _owner;

    internal RemoteDesktopSession(PortalRemoteDesktopModule owner, PortalBus bus, ObjectPath handle, string appId)
        : base(bus, handle, appId)
    {
        _owner = owner;
        Clipboard = new PortalClipboard(this);
    }

    /// <inheritdoc/>
    public ScreenCastState ScreenCast { get; } = new();

    /// <inheritdoc/>
    public PortalClipboard Clipboard { get; }

    /// <inheritdoc/>
    public PortalSession Session => this;

    /// <inheritdoc/>
    public bool IsStarted { get; internal set; }

    /// <summary>
    /// Gets the device kinds that the application asked for, limited to what the sink supports. Before <c>SelectDevices</c>, it holds every kind.
    /// </summary>
    public InputDeviceCapability Requested { get; internal set; } = InputDeviceCapability.Keyboard | InputDeviceCapability.Pointer | InputDeviceCapability.Touch;

    /// <summary>
    /// Gets the device kinds that the user or the restore data granted at start.
    /// </summary>
    public InputDeviceCapability Granted { get; internal set; }

    /// <summary>
    /// Gets the persist mode that the application asked for: 0 for none, 1 for this run, 2 until revoked.
    /// </summary>
    public uint PersistMode { get; internal set; }

    /// <summary>
    /// Gets the device kinds from the restore data, or <see langword="null"/> when the application gave none.
    /// </summary>
    public InputDeviceCapability? RestoreCandidate { get; internal set; }

    internal NotifyInjector? Injector { get; set; }

    internal EisReceiver? Eis { get; set; }

    /// <inheritdoc/>
    protected override void CloseCore() => _owner.Release(this);
}
