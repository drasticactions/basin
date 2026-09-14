using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Eis;
using Basin.Portal.DBus;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class PortalRemoteDesktopModule : PortalModule, IRemoteDesktopHandler, IRemoteDesktopProperties
{
    public const uint DeviceKeyboard = 1;

    public const uint DevicePointer = 2;

    public const uint DeviceTouchscreen = 4;

    private IPortalPrompts _prompts = null!;
    private OutputLayout _layout = null!;
    private IInputSink? _sink;
    private IActiveKeymap? _keymap;
    private IKeymapLookup? _keycodes;
    private PortalScreenCastModule? _screenCast;
    private PortalOptions _options = new();

    public override string WireInterface => "org.freedesktop.impl.portal.RemoteDesktop";

    public override int Version => 2;

    public override IReadOnlyList<Type> Capabilities => [typeof(IInputSink), typeof(IActiveKeymap), typeof(IKeymapLookup), typeof(IAppInfoResolver)];

    public override IReadOnlyList<Type> Drivers => [typeof(IPortalPrompts)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalRemoteDesktop;

    public uint AvailableDeviceTypes
    {
        get
        {
            if (_sink is null)
            {
                return 0;
            }

            var supports = _sink.Supports();
            var types = 0u;
            if ((supports & InputDeviceCapability.Keyboard) != 0)
            {
                types |= DeviceKeyboard;
            }

            if ((supports & InputDeviceCapability.Pointer) != 0)
            {
                types |= DevicePointer;
            }

            if ((supports & InputDeviceCapability.Touch) != 0)
            {
                types |= DeviceTouchscreen;
            }

            return types;
        }
    }

    uint IRemoteDesktopProperties.Version => (uint)Version;

    protected override void Bind(BasinServices services)
    {
        _prompts = services.Require<IPortalPrompts>();
        _layout = services.Require<OutputLayout>();
        _sink = services.Find<IInputSink>();
        _keymap = services.Find<IActiveKeymap>();
        _keycodes = services.Find<IKeymapLookup>();
        _screenCast = Bus?.Module<PortalScreenCastModule>();
        _options = services.Find<PortalOptions>() ?? new PortalOptions();
    }

    ValueTask IRemoteDesktopHandler.HandleGetPropertyAsync(IRemoteDesktopHandler.GetPropertyContext context) => context.Handle(this);

    ValueTask IRemoteDesktopHandler.HandleGetAllPropertiesAsync(IRemoteDesktopHandler.GetAllPropertiesContext context) => context.Handle(this);

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        var bus = Bus ?? throw new InvalidOperationException("the portal bus is not connected");
        var session = new RemoteDesktopSession(this, bus, sessionHandle, appId);
        bus.Register(session);
        return ValueTask.FromResult((PortalResponse.Success, Results.With("session_id", VariantValue.String(session.Id))));
    }

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> SelectDevicesAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not RemoteDesktopSession session || session.IsStarted)
        {
            return ValueTask.FromResult((PortalResponse.Other, Results.Empty));
        }

        var types = Vardict.UInt32(options, "types", DeviceKeyboard | DevicePointer | DeviceTouchscreen);
        session.Requested = ToCapability(types & AvailableDeviceTypes);
        session.PersistMode = Math.Min(Vardict.UInt32(options, "persist_mode"), 2);
        session.RestoreCandidate = null;
        if (Vardict.RestoreData(options, "restore_data") is { } restore &&
            string.Equals(restore.Vendor, _options.RestoreVendor, StringComparison.Ordinal) && restore.Version == RestoreData.Version &&
            restore.Data.Type == VariantValueType.Dictionary)
        {
            var data = restore.Data.GetDictionary<string, VariantValue>();
            if (data.TryGetValue("devices", out var devices) && devices.Type == VariantValueType.UInt32)
            {
                session.RestoreCandidate = ToCapability(devices.GetUInt32());
            }

            if (data.TryGetValue("sources", out _))
            {
                session.ScreenCast.RestoreCandidates = RestoreData.Decode(restore.Vendor, _options.RestoreVendor, restore.Version, restore.Data);
            }
        }

        return ValueTask.FromResult((PortalResponse.Success, Results.Empty));
    }

    public async ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> StartAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, string parentWindow, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not RemoteDesktopSession session || session.IsStarted)
        {
            return (PortalResponse.Other, Results.Empty);
        }

        if (_sink is null)
        {
            Log.Info($"remote desktop for {appId}: no input sink, nothing to grant");
            return (PortalResponse.Other, Results.Empty);
        }

        var request = TrackRequest(handle);
        try
        {
            var modal = Vardict.Bool(options, "modal", true);
            var offerPersist = session.PersistMode != 0;
            InputDeviceCapability granted;
            bool clipboard;
            uint persist;
            if (session.RestoreCandidate is { } candidate && (candidate & ~session.Requested) == 0 && candidate != 0)
            {
                granted = candidate;
                clipboard = session.Clipboard.Requested;
                persist = session.PersistMode;
                Log.Info($"remote desktop for {appId}: restored {granted} without a prompt");
            }
            else
            {
                var prompt = Named(new DevicePrompt(appId, parentWindow, modal, session.Requested, session.Clipboard.Requested, offerPersist));
                var answer = await _prompts.SelectDevices(prompt, request.Token).ConfigureAwait(true);
                if (!answer.IsAccepted || session.IsClosed)
                {
                    return (PortalScreenshotModule.ResponseFor(answer.Response, request), Results.Empty);
                }

                granted = answer.Value.Devices & session.Requested;
                clipboard = session.Clipboard.Requested && answer.Value.Clipboard;
                persist = Math.Min(answer.Value.PersistMode, session.PersistMode);
            }

            var results = new Dictionary<string, VariantValue>
            {
                ["devices"] = VariantValue.UInt32(ToTypes(granted)),
                ["clipboard_enabled"] = VariantValue.Bool(clipboard),
            };

            if (session.ScreenCast.SourcesSelected && _screenCast is { } screenCast)
            {
                var (response, streams) = await screenCast.StartStreamsAsync(session, request, appId, parentWindow, modal).ConfigureAwait(true);
                if (response != PortalResponse.Success || session.IsClosed)
                {
                    return (response, Results.Empty);
                }

                results["streams"] = streams["streams"];
            }

            session.Granted = granted;
            session.Clipboard.Enabled = clipboard;
            session.IsStarted = true;
            session.Injector = new NotifyInjector(_sink, _keycodes, _layout)
            {
                StreamBox = node => BoxOfStream(session, node),
            };
            if (persist != 0)
            {
                results["persist_mode"] = VariantValue.UInt32(persist);
                results["restore_data"] = EncodeRestore(session, granted);
            }

            Log.Info($"remote desktop for {appId}: granted {granted}{(clipboard ? " and the clipboard" : "")}");
            return (PortalResponse.Success, results);
        }
        finally
        {
            ReleaseRequest(request);
        }
    }

    public ValueTask NotifyPointerMotionAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, double dx, double dy)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.Motion(dx, dy);
        return default;
    }

    public ValueTask NotifyPointerMotionAbsoluteAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint stream, double x, double y)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.MotionAbsolute(stream, x, y);
        return default;
    }

    public ValueTask NotifyPointerButtonAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, int button, uint state)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.Button(button, state != 0);
        return default;
    }

    public ValueTask NotifyPointerAxisAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, double dx, double dy)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.Axis(dx, dy, Vardict.Bool(options, "finish"));
        return default;
    }

    public ValueTask NotifyPointerAxisDiscreteAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint axis, int steps)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.AxisDiscrete(axis, steps);
        return default;
    }

    public ValueTask NotifyKeyboardKeycodeAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, int keycode, uint state)
    {
        Injector(sessionHandle, InputDeviceCapability.Keyboard)?.Keycode(keycode, state != 0);
        return default;
    }

    public ValueTask NotifyKeyboardKeysymAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, int keysym, uint state)
    {
        Injector(sessionHandle, InputDeviceCapability.Keyboard)?.Keysym(keysym, state != 0);
        return default;
    }

    public ValueTask NotifyTouchDownAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint stream, uint slot, double x, double y)
    {
        Injector(sessionHandle, InputDeviceCapability.Touch)?.TouchDown(stream, slot, x, y);
        return default;
    }

    public ValueTask NotifyTouchMotionAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint stream, uint slot, double x, double y)
    {
        Injector(sessionHandle, InputDeviceCapability.Touch)?.TouchMotion(stream, slot, x, y);
        return default;
    }

    public ValueTask NotifyTouchUpAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint slot)
    {
        Injector(sessionHandle, InputDeviceCapability.Touch)?.TouchUp(slot);
        return default;
    }

    public ValueTask<SafeHandle> ConnectToEISAsync(ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not RemoteDesktopSession session || !session.IsStarted)
        {
            throw PortalError.Denied("the session has not started");
        }

        if (!EisLibrary.IsAvailable(out var whyNot))
        {
            throw PortalError.Unsupported(whyNot ?? "libeis is not installed");
        }

        if (_sink is null)
        {
            throw PortalError.Unsupported("this compositor injects no input");
        }

        if (session.Eis is null)
        {
            var receiver = new EisReceiver(Bus.Loop, _sink, _keymap, session.Granted);
            foreach (var stream in session.ScreenCast.Streams)
            {
                var box = stream.LayoutBox;
                receiver.AddRegion(stream.MappingId, box.X, box.Y, box.Width, box.Height, stream.Output?.Scale ?? 1);
            }

            if (session.ScreenCast.Streams.Count == 0)
            {
                foreach (var (output, _) in _layout.Outputs)
                {
                    var box = _layout.BoxOf(output);
                    receiver.AddRegion(output.Name, box.X, box.Y, box.Width, box.Height, output.Scale);
                }
            }

            session.Eis = receiver;
        }

        var fd = session.Eis.AddClientFd();
        Log.Info($"remote desktop for {appId}: eis client connected");
        return ValueTask.FromResult<SafeHandle>(new SafeFileHandle(fd, ownsHandle: true));
    }

    internal void Release(RemoteDesktopSession session)
    {
        session.Injector?.Dispose();
        session.Injector = null;
        session.Eis?.Dispose();
        session.Eis = null;
        session.Clipboard.Detach();
        _screenCast?.ReleaseStreams(session.ScreenCast);
    }

    private NotifyInjector? Injector(ObjectPath sessionHandle, InputDeviceCapability device)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not RemoteDesktopSession session || !session.IsStarted)
        {
            return null;
        }

        return (session.Granted & device) != 0 ? session.Injector : null;
    }

    private static Box? BoxOfStream(RemoteDesktopSession session, uint node)
    {
        foreach (var stream in session.ScreenCast.Streams)
        {
            if (stream.NodeId == node)
            {
                return stream.LayoutBox;
            }
        }

        return null;
    }

    private VariantValue EncodeRestore(RemoteDesktopSession session, InputDeviceCapability granted)
    {
        var data = new Dictionary<string, VariantValue> { ["devices"] = VariantValue.UInt32(ToTypes(granted)) };
        if (session.ScreenCast.Streams.Count > 0)
        {
            var sources = RestoreData.Encode(_options.RestoreVendor, session.ScreenCast.Streams).GetItem(2);
            if (sources.Type == VariantValueType.Variant)
            {
                sources = sources.GetVariantValue();
            }

            if (sources.Type == VariantValueType.Dictionary)
            {
                var inner = sources.GetDictionary<string, VariantValue>();
                if (inner.TryGetValue("sources", out var list))
                {
                    data["sources"] = list;
                }
            }
        }

        return Results.Restore(_options.RestoreVendor, RestoreData.Version, Results.Dict(data));
    }

    internal static InputDeviceCapability ToCapability(uint types)
    {
        var capability = InputDeviceCapability.None;
        if ((types & DeviceKeyboard) != 0)
        {
            capability |= InputDeviceCapability.Keyboard;
        }

        if ((types & DevicePointer) != 0)
        {
            capability |= InputDeviceCapability.Pointer;
        }

        if ((types & DeviceTouchscreen) != 0)
        {
            capability |= InputDeviceCapability.Touch;
        }

        return capability;
    }

    internal static uint ToTypes(InputDeviceCapability capability)
    {
        var types = 0u;
        if ((capability & InputDeviceCapability.Keyboard) != 0)
        {
            types |= DeviceKeyboard;
        }

        if ((capability & InputDeviceCapability.Pointer) != 0)
        {
            types |= DevicePointer;
        }

        if ((capability & InputDeviceCapability.Touch) != 0)
        {
            types |= DeviceTouchscreen;
        }

        return types;
    }
}
