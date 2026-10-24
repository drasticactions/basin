using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// Serves <c>org.freedesktop.impl.portal.Clipboard</c> on the sessions of the RemoteDesktop and InputCapture modules.
/// </summary>
public sealed class PortalClipboardModule : PortalModule, IClipboardHandler, IClipboardProperties
{
    private ISelectionStore _store = null!;

    /// <inheritdoc/>
    public override string WireInterface => "org.freedesktop.impl.portal.Clipboard";

    /// <inheritdoc/>
    public override int Version => 1;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(ISelectionStore)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalClipboard;

    uint IClipboardProperties.Version => (uint)Version;

    /// <summary>
    /// Offers the interface only when a bus and the RemoteDesktop or InputCapture module are installed. The clipboard has no session of its own.
    /// </summary>
    /// <param name="services">The registry after all defaults are seeded.</param>
    /// <returns><see langword="false"/> when no bus or no carrier module is installed.</returns>
    public override bool ShouldInstall(BasinServices services)
    {
        if (!base.ShouldInstall(services))
        {
            return false;
        }

        if (services.Modules.ContainsKey("org.freedesktop.impl.portal.RemoteDesktop") ||
            services.Modules.ContainsKey("org.freedesktop.impl.portal.InputCapture"))
        {
            return true;
        }

        Log.Info($"{WireInterface} not offered, neither RemoteDesktop nor InputCapture is installed");
        return false;
    }

    /// <inheritdoc/>
    protected override void Bind(BasinServices services) => _store = services.Require<ISelectionStore>();

    ValueTask IClipboardHandler.HandleGetPropertyAsync(IClipboardHandler.GetPropertyContext context) => context.Handle(this);

    ValueTask IClipboardHandler.HandleGetAllPropertiesAsync(IClipboardHandler.GetAllPropertiesContext context) => context.Handle(this);

    /// <summary>
    /// Marks the clipboard as requested on a session that has not started. A request after the start is ignored.
    /// </summary>
    /// <param name="sessionHandle">The RemoteDesktop or InputCapture session.</param>
    /// <param name="options">No options are read.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="DBusErrorReplyException">No carrier session has that path.</exception>
    public ValueTask RequestClipboardAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not IClipboardCarrier carrier)
        {
            throw PortalError.NoObject(sessionHandle.ToString());
        }

        if (carrier.IsStarted)
        {
            Log.Info($"clipboard: requested on session {carrier.Session.Id} after it started, ignored");
            return default;
        }

        carrier.Clipboard.Requested = true;
        carrier.Clipboard.Attach(_store);
        return default;
    }

    /// <summary>
    /// Makes the session the owner of the compositor clipboard with the MIME types in the <c>mime_types</c> option.
    /// </summary>
    /// <param name="sessionHandle">A started session with the clipboard enabled.</param>
    /// <param name="options">The <c>mime_types</c> option.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="DBusErrorReplyException">The session does not exist, has no clipboard access, or the store refuses the selection.</exception>
    public ValueTask SetSelectionAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options)
    {
        var clipboard = Enabled(sessionHandle);
        var mimeTypes = Vardict.Strings(options, "mime_types");
        if (!clipboard.SetSelection(mimeTypes))
        {
            throw PortalError.Denied("The selection was refused.");
        }

        return default;
    }

    /// <summary>
    /// Gives the session the descriptor of a pending transfer to write to.
    /// </summary>
    /// <param name="sessionHandle">A started session with the clipboard enabled.</param>
    /// <param name="serial">The serial from <c>SelectionTransfer</c>.</param>
    /// <returns>A descriptor that the caller owns.</returns>
    /// <exception cref="DBusErrorReplyException">The session does not exist, has no clipboard access, or the serial is unknown.</exception>
    public ValueTask<SafeHandle> SelectionWriteAsync(ObjectPath sessionHandle, uint serial) =>
        ValueTask.FromResult(Enabled(sessionHandle).SelectionWrite(serial));

    /// <summary>
    /// Closes the descriptor of a finished transfer.
    /// </summary>
    /// <param name="sessionHandle">A started session with the clipboard enabled.</param>
    /// <param name="serial">The serial from <c>SelectionTransfer</c>.</param>
    /// <param name="success">The result that the session reports.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="DBusErrorReplyException">The session does not exist or has no clipboard access.</exception>
    public ValueTask SelectionWriteDoneAsync(ObjectPath sessionHandle, uint serial, bool success)
    {
        Enabled(sessionHandle).SelectionWriteDone(serial, success);
        return default;
    }

    /// <summary>
    /// Opens a pipe that the current clipboard owner fills with one MIME type.
    /// </summary>
    /// <param name="sessionHandle">A started session with the clipboard enabled.</param>
    /// <param name="mimeType">The MIME type to read.</param>
    /// <returns>The read end of the pipe, which the caller owns.</returns>
    /// <exception cref="DBusErrorReplyException">The session does not exist, has no clipboard access, or the pipe cannot be created.</exception>
    public ValueTask<SafeHandle> SelectionReadAsync(ObjectPath sessionHandle, string mimeType) =>
        ValueTask.FromResult(Enabled(sessionHandle).SelectionRead(mimeType));

    private PortalClipboard Enabled(ObjectPath sessionHandle)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not IClipboardCarrier carrier)
        {
            throw PortalError.NoObject(sessionHandle.ToString());
        }

        if (!carrier.IsStarted || !carrier.Clipboard.Enabled)
        {
            throw PortalError.Denied("This session has no clipboard access.");
        }

        carrier.Clipboard.Attach(_store);
        return carrier.Clipboard;
    }
}
