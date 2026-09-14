using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class PortalClipboardModule : PortalModule, IClipboardHandler, IClipboardProperties
{
    private ISelectionStore _store = null!;

    public override string WireInterface => "org.freedesktop.impl.portal.Clipboard";

    public override int Version => 1;

    public override IReadOnlyList<Type> Drivers => [typeof(ISelectionStore)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalClipboard;

    uint IClipboardProperties.Version => (uint)Version;

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

        Log.Info($"{WireInterface} not offered: neither RemoteDesktop nor InputCapture installed, and the clipboard rides on their sessions");
        return false;
    }

    protected override void Bind(BasinServices services) => _store = services.Require<ISelectionStore>();

    ValueTask IClipboardHandler.HandleGetPropertyAsync(IClipboardHandler.GetPropertyContext context) => context.Handle(this);

    ValueTask IClipboardHandler.HandleGetAllPropertiesAsync(IClipboardHandler.GetAllPropertiesContext context) => context.Handle(this);

    public ValueTask RequestClipboardAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not IClipboardCarrier carrier)
        {
            throw PortalError.NoObject(sessionHandle.ToString());
        }

        if (carrier.IsStarted)
        {
            Log.Info($"clipboard requested on session {carrier.Session.Id} after it started; ignored");
            return default;
        }

        carrier.Clipboard.Requested = true;
        carrier.Clipboard.Attach(_store);
        return default;
    }

    public ValueTask SetSelectionAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options)
    {
        var clipboard = Enabled(sessionHandle);
        var mimeTypes = Vardict.Strings(options, "mime_types");
        if (!clipboard.SetSelection(mimeTypes))
        {
            throw PortalError.Denied("the selection was refused");
        }

        return default;
    }

    public ValueTask<SafeHandle> SelectionWriteAsync(ObjectPath sessionHandle, uint serial) =>
        ValueTask.FromResult(Enabled(sessionHandle).SelectionWrite(serial));

    public ValueTask SelectionWriteDoneAsync(ObjectPath sessionHandle, uint serial, bool success)
    {
        Enabled(sessionHandle).SelectionWriteDone(serial, success);
        return default;
    }

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
            throw PortalError.Denied("clipboard access was not given to this session");
        }

        carrier.Clipboard.Attach(_store);
        return carrier.Clipboard;
    }
}
