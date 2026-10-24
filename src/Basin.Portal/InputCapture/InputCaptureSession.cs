using Basin.Capabilities;
using Basin.Eis;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// An InputCapture portal session. It sends the capture state changes from its <see cref="Engine"/> to the frontend as D-Bus signals.
/// </summary>
public sealed class InputCaptureSession : PortalSession, IInputCaptureFront, IClipboardCarrier
{
    private readonly PortalInputCaptureModule _owner;

    internal InputCaptureSession(PortalInputCaptureModule owner, PortalBus bus, ObjectPath handle, string appId)
        : base(bus, handle, appId)
    {
        _owner = owner;
        Clipboard = new PortalClipboard(this);
    }

    /// <inheritdoc/>
    public PortalClipboard Clipboard { get; }

    /// <inheritdoc/>
    public PortalSession Session => this;

    /// <inheritdoc/>
    public bool IsStarted { get; internal set; }

    /// <summary>
    /// Gets the device kinds that the application asked for.
    /// </summary>
    public InputDeviceCapability Requested { get; internal set; }

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

    /// <summary>
    /// Gets the capture handle after the start. It is <see langword="null"/> before the start and after the close.
    /// </summary>
    public IInputCaptureHandle? Engine { get; internal set; }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void Disabled() => Emit(connection => connection.EmitDisabled(new ObjectPath(PortalBus.RootPath), Handle, []));

    /// <inheritdoc/>
    public void ZonesChanged() => Emit(connection => connection.EmitZonesChanged(new ObjectPath(PortalBus.RootPath), Handle, []));

    /// <inheritdoc/>
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
