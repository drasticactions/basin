using System.Runtime.InteropServices;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

internal sealed class PortalHandler : DBusHandler,
    IScreenCastHandler,
    IRemoteDesktopHandler,
    IScreenshotHandler,
    IClipboardHandler,
    IInputCaptureHandler,
    IGlobalShortcutsHandler,
    IAccessHandler,
    IRequestHandler,
    ISessionHandler,
    ISessionProperties
{
    private const DBusInterface RootInterfaces =
        DBusInterface.OrgFreedesktopImplPortalScreenCast |
        DBusInterface.OrgFreedesktopImplPortalRemoteDesktop |
        DBusInterface.OrgFreedesktopImplPortalScreenshot |
        DBusInterface.OrgFreedesktopImplPortalClipboard |
        DBusInterface.OrgFreedesktopImplPortalInputCapture |
        DBusInterface.OrgFreedesktopImplPortalGlobalShortcuts |
        DBusInterface.OrgFreedesktopImplPortalAccess;

    private readonly PortalBus _bus;

    public PortalHandler(PortalBus bus)
        : base(null!, PortalBus.RootPath, handlesChildPaths: true, bus.Context)
    {
        _bus = bus;
    }

    public DBusInterface Installed { get; set; }

    uint ISessionProperties.Version => 1;

    protected override async ValueTask InvokeAsync(DBusMethod method, MethodContext context)
    {
        var sender = context.Request.SenderAsString;
        var path = context.Request.PathAsString;
        if (!_bus.AcceptSender(sender))
        {
            Log.Warn($"{sender} called {context.Request.InterfaceAsString}.{context.Request.MemberAsString} and is not the portal frontend");
            context.ReplyError(PortalError.AccessDenied, "only the portal frontend may call this backend");
            return;
        }

        _bus.Current = new PortalCall(sender ?? "", path ?? "");
        await base.InvokeAsync(method, context).ConfigureAwait(false);
    }

    protected override void HandleException(MethodContext context, Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            context.ReplyError("org.freedesktop.portal.Error.Cancelled", "the request was closed");
            return;
        }

        if (ex is not DBusErrorReplyException)
        {
            Log.Warn($"{context.Request.InterfaceAsString}.{context.Request.MemberAsString} failed: {ex}");
        }

        base.HandleException(context, ex);
    }

    protected override bool SupportsInterface(DBusInterface dbusInterface, ReadOnlySpan<char> path)
    {
        if (path.SequenceEqual(PortalBus.RootPath))
        {
            return (dbusInterface & RootInterfaces) != 0;
        }

        if (path.StartsWith(PortalBus.RootPath + "/request/"))
        {
            return dbusInterface == DBusInterface.OrgFreedesktopImplPortalRequest;
        }

        if (path.StartsWith(PortalBus.RootPath + "/session/"))
        {
            return dbusInterface == DBusInterface.OrgFreedesktopImplPortalSession;
        }

        return false;
    }

    protected override ValueTask HandleIntrospectAsync(IntrospectContext context)
    {
        var path = context.MethodContext.Request.PathAsString;
        var interfaces = GetSupportedInterfaces(path.AsSpan());
        if (path == PortalBus.RootPath)
        {
            interfaces &= Installed;
        }

        context.Reply(interfaces);
        return default;
    }

    private T Module<T>(string name)
        where T : class
    {
        foreach (var module in _bus.Modules)
        {
            if (module is T typed)
            {
                return typed;
            }
        }

        throw PortalError.NoInterface(name);
    }

    private bool TryModule<T>(out T module)
        where T : class
    {
        foreach (var candidate in _bus.Modules)
        {
            if (candidate is T typed)
            {
                module = typed;
                return true;
            }
        }

        module = null!;
        return false;
    }

    private static void NoInterface(MethodContext context, string name) =>
        context.ReplyError(PortalError.UnknownInterface, $"{name} is not installed on this compositor");

    ValueTask IScreenCastHandler.HandleGetPropertyAsync(IScreenCastHandler.GetPropertyContext context)
    {
        if (TryModule<IScreenCastProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.ScreenCast");
        return default;
    }

    ValueTask IScreenCastHandler.HandleGetAllPropertiesAsync(IScreenCastHandler.GetAllPropertiesContext context)
    {
        if (TryModule<IScreenCastProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.ScreenCast");
        return default;
    }

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IScreenCastHandler.CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IScreenCastHandler>("ScreenCast").CreateSessionAsync(handle, sessionHandle, appId, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IScreenCastHandler.SelectSourcesAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IScreenCastHandler>("ScreenCast").SelectSourcesAsync(handle, sessionHandle, appId, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IScreenCastHandler.StartAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, string parentWindow, Dictionary<string, VariantValue> options) =>
        Module<IScreenCastHandler>("ScreenCast").StartAsync(handle, sessionHandle, appId, parentWindow, options);

    ValueTask IRemoteDesktopHandler.HandleGetPropertyAsync(IRemoteDesktopHandler.GetPropertyContext context)
    {
        if (TryModule<IRemoteDesktopProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.RemoteDesktop");
        return default;
    }

    ValueTask IRemoteDesktopHandler.HandleGetAllPropertiesAsync(IRemoteDesktopHandler.GetAllPropertiesContext context)
    {
        if (TryModule<IRemoteDesktopProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.RemoteDesktop");
        return default;
    }

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IRemoteDesktopHandler.CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").CreateSessionAsync(handle, sessionHandle, appId, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IRemoteDesktopHandler.SelectDevicesAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").SelectDevicesAsync(handle, sessionHandle, appId, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IRemoteDesktopHandler.StartAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, string parentWindow, Dictionary<string, VariantValue> options) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").StartAsync(handle, sessionHandle, appId, parentWindow, options);

    ValueTask IRemoteDesktopHandler.NotifyPointerMotionAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, double dx, double dy) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyPointerMotionAsync(sessionHandle, options, dx, dy);

    ValueTask IRemoteDesktopHandler.NotifyPointerMotionAbsoluteAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint stream, double x, double y) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyPointerMotionAbsoluteAsync(sessionHandle, options, stream, x, y);

    ValueTask IRemoteDesktopHandler.NotifyPointerButtonAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, int button, uint state) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyPointerButtonAsync(sessionHandle, options, button, state);

    ValueTask IRemoteDesktopHandler.NotifyPointerAxisAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, double dx, double dy) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyPointerAxisAsync(sessionHandle, options, dx, dy);

    ValueTask IRemoteDesktopHandler.NotifyPointerAxisDiscreteAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint axis, int steps) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyPointerAxisDiscreteAsync(sessionHandle, options, axis, steps);

    ValueTask IRemoteDesktopHandler.NotifyKeyboardKeycodeAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, int keycode, uint state) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyKeyboardKeycodeAsync(sessionHandle, options, keycode, state);

    ValueTask IRemoteDesktopHandler.NotifyKeyboardKeysymAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, int keysym, uint state) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyKeyboardKeysymAsync(sessionHandle, options, keysym, state);

    ValueTask IRemoteDesktopHandler.NotifyTouchDownAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint stream, uint slot, double x, double y) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyTouchDownAsync(sessionHandle, options, stream, slot, x, y);

    ValueTask IRemoteDesktopHandler.NotifyTouchMotionAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint stream, uint slot, double x, double y) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyTouchMotionAsync(sessionHandle, options, stream, slot, x, y);

    ValueTask IRemoteDesktopHandler.NotifyTouchUpAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint slot) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").NotifyTouchUpAsync(sessionHandle, options, slot);

    ValueTask<SafeHandle> IRemoteDesktopHandler.ConnectToEISAsync(ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IRemoteDesktopHandler>("RemoteDesktop").ConnectToEISAsync(sessionHandle, appId, options);

    ValueTask IScreenshotHandler.HandleGetPropertyAsync(IScreenshotHandler.GetPropertyContext context)
    {
        if (TryModule<IScreenshotProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.Screenshot");
        return default;
    }

    ValueTask IScreenshotHandler.HandleGetAllPropertiesAsync(IScreenshotHandler.GetAllPropertiesContext context)
    {
        if (TryModule<IScreenshotProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.Screenshot");
        return default;
    }

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IScreenshotHandler.ScreenshotAsync(
        ObjectPath handle, string appId, string parentWindow, Dictionary<string, VariantValue> options) =>
        Module<IScreenshotHandler>("Screenshot").ScreenshotAsync(handle, appId, parentWindow, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IScreenshotHandler.PickColorAsync(
        ObjectPath handle, string appId, string parentWindow, Dictionary<string, VariantValue> options) =>
        Module<IScreenshotHandler>("Screenshot").PickColorAsync(handle, appId, parentWindow, options);

    ValueTask IClipboardHandler.HandleGetPropertyAsync(IClipboardHandler.GetPropertyContext context)
    {
        if (TryModule<IClipboardProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.Clipboard");
        return default;
    }

    ValueTask IClipboardHandler.HandleGetAllPropertiesAsync(IClipboardHandler.GetAllPropertiesContext context)
    {
        if (TryModule<IClipboardProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.Clipboard");
        return default;
    }

    ValueTask IClipboardHandler.RequestClipboardAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options) =>
        Module<IClipboardHandler>("Clipboard").RequestClipboardAsync(sessionHandle, options);

    ValueTask IClipboardHandler.SetSelectionAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options) =>
        Module<IClipboardHandler>("Clipboard").SetSelectionAsync(sessionHandle, options);

    ValueTask<SafeHandle> IClipboardHandler.SelectionWriteAsync(ObjectPath sessionHandle, uint serial) =>
        Module<IClipboardHandler>("Clipboard").SelectionWriteAsync(sessionHandle, serial);

    ValueTask IClipboardHandler.SelectionWriteDoneAsync(ObjectPath sessionHandle, uint serial, bool success) =>
        Module<IClipboardHandler>("Clipboard").SelectionWriteDoneAsync(sessionHandle, serial, success);

    ValueTask<SafeHandle> IClipboardHandler.SelectionReadAsync(ObjectPath sessionHandle, string mimeType) =>
        Module<IClipboardHandler>("Clipboard").SelectionReadAsync(sessionHandle, mimeType);

    ValueTask IInputCaptureHandler.HandleGetPropertyAsync(IInputCaptureHandler.GetPropertyContext context)
    {
        if (TryModule<IInputCaptureProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.InputCapture");
        return default;
    }

    ValueTask IInputCaptureHandler.HandleGetAllPropertiesAsync(IInputCaptureHandler.GetAllPropertiesContext context)
    {
        if (TryModule<IInputCaptureProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.InputCapture");
        return default;
    }

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IInputCaptureHandler.CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, string parentWindow, Dictionary<string, VariantValue> options) =>
        Module<IInputCaptureHandler>("InputCapture").CreateSessionAsync(handle, sessionHandle, appId, parentWindow, options);

    ValueTask<Dictionary<string, VariantValue>> IInputCaptureHandler.CreateSession2Async(
        ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IInputCaptureHandler>("InputCapture").CreateSession2Async(sessionHandle, appId, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IInputCaptureHandler.StartAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, string parentWindow, Dictionary<string, VariantValue> options) =>
        Module<IInputCaptureHandler>("InputCapture").StartAsync(handle, sessionHandle, appId, parentWindow, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IInputCaptureHandler.GetZonesAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IInputCaptureHandler>("InputCapture").GetZonesAsync(handle, sessionHandle, appId, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IInputCaptureHandler.SetPointerBarriersAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options,
        Dictionary<string, VariantValue>[] barriers, uint zoneSet) =>
        Module<IInputCaptureHandler>("InputCapture").SetPointerBarriersAsync(handle, sessionHandle, appId, options, barriers, zoneSet);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IInputCaptureHandler.EnableAsync(
        ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IInputCaptureHandler>("InputCapture").EnableAsync(sessionHandle, appId, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IInputCaptureHandler.DisableAsync(
        ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IInputCaptureHandler>("InputCapture").DisableAsync(sessionHandle, appId, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IInputCaptureHandler.ReleaseAsync(
        ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IInputCaptureHandler>("InputCapture").ReleaseAsync(sessionHandle, appId, options);

    ValueTask<SafeHandle> IInputCaptureHandler.ConnectToEISAsync(ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IInputCaptureHandler>("InputCapture").ConnectToEISAsync(sessionHandle, appId, options);

    ValueTask IGlobalShortcutsHandler.HandleGetPropertyAsync(IGlobalShortcutsHandler.GetPropertyContext context)
    {
        if (TryModule<IGlobalShortcutsProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.GlobalShortcuts");
        return default;
    }

    ValueTask IGlobalShortcutsHandler.HandleGetAllPropertiesAsync(IGlobalShortcutsHandler.GetAllPropertiesContext context)
    {
        if (TryModule<IGlobalShortcutsProperties>(out var properties))
        {
            return context.Handle(properties);
        }

        NoInterface(context.MethodContext, "org.freedesktop.impl.portal.GlobalShortcuts");
        return default;
    }

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IGlobalShortcutsHandler.CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options) =>
        Module<IGlobalShortcutsHandler>("GlobalShortcuts").CreateSessionAsync(handle, sessionHandle, appId, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IGlobalShortcutsHandler.BindShortcutsAsync(
        ObjectPath handle, ObjectPath sessionHandle, (string, Dictionary<string, VariantValue>)[] shortcuts, string parentWindow,
        Dictionary<string, VariantValue> options) =>
        Module<IGlobalShortcutsHandler>("GlobalShortcuts").BindShortcutsAsync(handle, sessionHandle, shortcuts, parentWindow, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IGlobalShortcutsHandler.ListShortcutsAsync(
        ObjectPath handle, ObjectPath sessionHandle) =>
        Module<IGlobalShortcutsHandler>("GlobalShortcuts").ListShortcutsAsync(handle, sessionHandle);

    ValueTask IGlobalShortcutsHandler.ConfigureShortcutsAsync(ObjectPath sessionHandle, string parentWindow, Dictionary<string, VariantValue> options) =>
        Module<IGlobalShortcutsHandler>("GlobalShortcuts").ConfigureShortcutsAsync(sessionHandle, parentWindow, options);

    ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> IAccessHandler.AccessDialogAsync(
        ObjectPath handle, string appId, string parentWindow, string title, string subtitle, string body, Dictionary<string, VariantValue> options) =>
        Module<IAccessHandler>("Access").AccessDialogAsync(handle, appId, parentWindow, title, subtitle, body, options);

    ValueTask IRequestHandler.CloseAsync()
    {
        var path = _bus.Current.Path;
        if (!_bus.CloseRequest(path))
        {
            Log.Debug($"Close on unknown request {path}");
        }

        return default;
    }

    ValueTask ISessionHandler.HandleGetPropertyAsync(ISessionHandler.GetPropertyContext context) =>
        _bus.SessionAt(_bus.Current.Path) is not null
            ? context.Handle(this)
            : throw PortalError.NoObject(_bus.Current.Path);

    ValueTask ISessionHandler.HandleGetAllPropertiesAsync(ISessionHandler.GetAllPropertiesContext context) =>
        _bus.SessionAt(_bus.Current.Path) is not null
            ? context.Handle(this)
            : throw PortalError.NoObject(_bus.Current.Path);

    ValueTask ISessionHandler.CloseAsync()
    {
        var path = _bus.Current.Path;
        if (!_bus.CloseSession(path))
        {
            throw PortalError.NoObject(path);
        }

        return default;
    }
}
