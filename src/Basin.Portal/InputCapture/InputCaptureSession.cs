using Basin.Capabilities;
using Basin.Eis;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class InputCaptureSession : PortalSession, IInputCaptureFront, IClipboardCarrier
{
    private readonly PortalInputCaptureModule _owner;

    internal InputCaptureSession(PortalInputCaptureModule owner, PortalBus bus, ObjectPath handle, string appId)
        : base(bus, handle, appId)
    {
        _owner = owner;
        Clipboard = new PortalClipboard(this);
    }

    public PortalClipboard Clipboard { get; }

    public PortalSession Session => this;

    public bool IsStarted { get; internal set; }

    public InputDeviceCapability Requested { get; internal set; }

    public InputDeviceCapability Granted { get; internal set; }

    public uint PersistMode { get; internal set; }

    public InputDeviceCapability? RestoreCandidate { get; internal set; }

    public IInputCaptureHandle? Engine { get; internal set; }

    public void Activated(uint activationId, double x, double y, uint barrierId)
    {
        var options = new Dictionary<string, VariantValue>
        {
            ["activation_id"] = VariantValue.UInt32(activationId),
            ["cursor_position"] = VariantValue.Struct(VariantValue.Double(x), VariantValue.Double(y)),
        };
        if (barrierId != 0)
        {
            options["barrier_id"] = VariantValue.UInt32(barrierId);
        }

        Emit(connection => connection.EmitActivated(new ObjectPath(PortalBus.RootPath), Handle, options));
    }

    public void Deactivated(uint activationId)
    {
        var options = new Dictionary<string, VariantValue>
        {
            ["activation_id"] = VariantValue.UInt32(activationId),
        };
        if (_owner.CursorPosition is { } position)
        {
            options["cursor_position"] = VariantValue.Struct(VariantValue.Double(position.X), VariantValue.Double(position.Y));
        }

        Emit(connection => connection.EmitDeactivated(new ObjectPath(PortalBus.RootPath), Handle, options));
    }

    public void Disabled() => Emit(connection => connection.EmitDisabled(new ObjectPath(PortalBus.RootPath), Handle, []));

    public void ZonesChanged() => Emit(connection => connection.EmitZonesChanged(new ObjectPath(PortalBus.RootPath), Handle, []));

    protected override void CloseCore() => _owner.Release(this);

    private void Emit(Action<DBusConnection> emit)
    {
        if (Bus.Connection is not { } connection || IsClosed)
        {
            return;
        }

        try
        {
            emit(connection);
        }
        catch (Exception e) when (e is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug($"input capture session {Id}: signal not sent: {e.Message}");
        }
    }
}
