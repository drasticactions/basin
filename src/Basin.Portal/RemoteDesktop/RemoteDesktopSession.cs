using Basin.Capabilities;
using Basin.Eis;
using Tmds.DBus.Protocol;

namespace Basin.Portal;

public sealed class RemoteDesktopSession : PortalSession, IScreenCastCarrier, IClipboardCarrier
{
    private readonly PortalRemoteDesktopModule _owner;

    internal RemoteDesktopSession(PortalRemoteDesktopModule owner, PortalBus bus, ObjectPath handle, string appId)
        : base(bus, handle, appId)
    {
        _owner = owner;
        Clipboard = new PortalClipboard(this);
    }

    public ScreenCastState ScreenCast { get; } = new();

    public PortalClipboard Clipboard { get; }

    public PortalSession Session => this;

    public bool IsStarted { get; internal set; }

    public InputDeviceCapability Requested { get; internal set; } = InputDeviceCapability.Keyboard | InputDeviceCapability.Pointer | InputDeviceCapability.Touch;

    public InputDeviceCapability Granted { get; internal set; }

    public uint PersistMode { get; internal set; }

    public InputDeviceCapability? RestoreCandidate { get; internal set; }

    internal NotifyInjector? Injector { get; set; }

    internal EisReceiver? Eis { get; set; }

    protected override void CloseCore() => _owner.Release(this);
}
