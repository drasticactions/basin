using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Eis;
using Basin.Portal.DBus;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// Serves <c>org.freedesktop.impl.portal.RemoteDesktop</c>. It injects input through <see cref="IInputSink"/>, from <c>Notify*</c> calls or from an EIS connection, and can carry a screen cast.
/// </summary>
public sealed class PortalRemoteDesktopModule : PortalModule, IRemoteDesktopHandler, IRemoteDesktopProperties
{
    /// <summary>
    /// The portal device type bit for the keyboard.
    /// </summary>
    public const uint DeviceKeyboard = 1;

    /// <summary>
    /// The portal device type bit for the pointer.
    /// </summary>
    public const uint DevicePointer = 2;

    /// <summary>
    /// The portal device type bit for the touchscreen.
    /// </summary>
    public const uint DeviceTouchscreen = 4;

    private IPortalPrompts _prompts = null!;
    private OutputLayout _layout = null!;
    private IInputSink? _sink;
    private IActiveKeymap? _keymap;
    private IKeymapLookup? _keycodes;
    private PortalScreenCastModule? _screenCast;
    private PortalOptions _options = new();

    /// <inheritdoc/>
    public override string WireInterface => "org.freedesktop.impl.portal.RemoteDesktop";

    /// <inheritdoc/>
    public override int Version => 2;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IInputSink), typeof(IActiveKeymap), typeof(IKeymapLookup), typeof(IAppInfoResolver)];

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(IPortalPrompts)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalRemoteDesktop;

    /// <summary>
    /// Gets the device type bits that the <see cref="IInputSink"/> supports. It is 0 when no sink is registered.
    /// </summary>
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

    /// <inheritdoc/>
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

    /// <summary>
    /// Creates a remote desktop session.
    /// </summary>
    /// <param name="handle">The request object path.</param>
    /// <param name="sessionHandle">The session object path.</param>
    /// <param name="appId">The application that owns the session.</param>
    /// <param name="options">No options are read.</param>
    /// <returns><see cref="PortalResponse.Success"/> with the <c>session_id</c> result.</returns>
    /// <exception cref="InvalidOperationException">The module is not installed.</exception>
    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        var bus = Bus ?? throw new InvalidOperationException("The portal bus is not connected.");
        var session = new RemoteDesktopSession(this, bus, sessionHandle, appId);
        bus.Register(session);
        return ValueTask.FromResult((PortalResponse.Success, Results.With("session_id", VariantValue.String(session.Id))));
    }

    /// <summary>
    /// Records the device types, the persist mode and the restore data for a session that has not started.
    /// </summary>
    /// <param name="handle">The request object path.</param>
    /// <param name="sessionHandle">The remote desktop session.</param>
    /// <param name="appId">The application that owns the session.</param>
    /// <param name="options">The <c>types</c>, <c>persist_mode</c> and <c>restore_data</c> options.</param>
    /// <returns><see cref="PortalResponse.Success"/>, or <see cref="PortalResponse.Other"/> when the session does not exist or has started.</returns>
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

    /// <summary>
    /// Starts a session. Restore data that covers the request skips the device prompt. A selected screen cast starts its streams in the same call.
    /// </summary>
    /// <param name="handle">The request object path.</param>
    /// <param name="sessionHandle">The remote desktop session.</param>
    /// <param name="appId">The application that owns the session.</param>
    /// <param name="parentWindow">The parent window identifier of the application.</param>
    /// <param name="options">The <c>modal</c> option.</param>
    /// <returns>The granted <c>devices</c>, the clipboard state, any <c>streams</c> and the restore data, or a cancel or other response.</returns>
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
                    return (PortalRefusal.RefusalFor(answer.Response), Results.Empty);
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

    /// <summary>
    /// Moves the pointer by a relative amount when the session has the pointer.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">No options are read.</param>
    /// <param name="dx">The horizontal motion in logical pixels.</param>
    /// <param name="dy">The vertical motion in logical pixels.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyPointerMotionAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, double dx, double dy)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.Motion(dx, dy);
        return default;
    }

    /// <summary>
    /// Moves the pointer to a position in a screen cast stream when the session has the pointer.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">No options are read.</param>
    /// <param name="stream">The PipeWire node id of the stream.</param>
    /// <param name="x">The x position in the stream, in logical pixels.</param>
    /// <param name="y">The y position in the stream, in logical pixels.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyPointerMotionAbsoluteAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint stream, double x, double y)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.MotionAbsolute(stream, x, y);
        return default;
    }

    /// <summary>
    /// Presses or releases a pointer button when the session has the pointer.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">No options are read.</param>
    /// <param name="button">The evdev button code.</param>
    /// <param name="state">1 to press, 0 to release.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyPointerButtonAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, int button, uint state)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.Button(button, state != 0);
        return default;
    }

    /// <summary>
    /// Sends a smooth scroll when the session has the pointer.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">The <c>finish</c> option, which ends the scroll.</param>
    /// <param name="dx">The horizontal scroll distance.</param>
    /// <param name="dy">The vertical scroll distance.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyPointerAxisAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, double dx, double dy)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.Axis(dx, dy, Vardict.Bool(options, "finish"));
        return default;
    }

    /// <summary>
    /// Sends wheel steps when the session has the pointer.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">No options are read.</param>
    /// <param name="axis">0 for vertical, 1 for horizontal.</param>
    /// <param name="steps">The number of wheel steps, with the sign for the direction.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyPointerAxisDiscreteAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint axis, int steps)
    {
        Injector(sessionHandle, InputDeviceCapability.Pointer)?.AxisDiscrete(axis, steps);
        return default;
    }

    /// <summary>
    /// Presses or releases a key by evdev keycode when the session has the keyboard.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">No options are read.</param>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="state">1 to press, 0 to release.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyKeyboardKeycodeAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, int keycode, uint state)
    {
        Injector(sessionHandle, InputDeviceCapability.Keyboard)?.Keycode(keycode, state != 0);
        return default;
    }

    /// <summary>
    /// Presses or releases the key that types a keysym when the session has the keyboard. A keysym with no key in the keymap is ignored.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">No options are read.</param>
    /// <param name="keysym">The XKB keysym.</param>
    /// <param name="state">1 to press, 0 to release.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyKeyboardKeysymAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, int keysym, uint state)
    {
        Injector(sessionHandle, InputDeviceCapability.Keyboard)?.Keysym(keysym, state != 0);
        return default;
    }

    /// <summary>
    /// Starts a touch point in a screen cast stream when the session has the touchscreen.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">No options are read.</param>
    /// <param name="stream">The PipeWire node id of the stream.</param>
    /// <param name="slot">The touch slot.</param>
    /// <param name="x">The x position in the stream, in logical pixels.</param>
    /// <param name="y">The y position in the stream, in logical pixels.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyTouchDownAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint stream, uint slot, double x, double y)
    {
        Injector(sessionHandle, InputDeviceCapability.Touch)?.TouchDown(stream, slot, x, y);
        return default;
    }

    /// <summary>
    /// Moves a touch point in a screen cast stream when the session has the touchscreen.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">No options are read.</param>
    /// <param name="stream">The PipeWire node id of the stream.</param>
    /// <param name="slot">The touch slot.</param>
    /// <param name="x">The x position in the stream, in logical pixels.</param>
    /// <param name="y">The y position in the stream, in logical pixels.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyTouchMotionAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint stream, uint slot, double x, double y)
    {
        Injector(sessionHandle, InputDeviceCapability.Touch)?.TouchMotion(stream, slot, x, y);
        return default;
    }

    /// <summary>
    /// Ends a touch point when the session has the touchscreen.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="options">No options are read.</param>
    /// <param name="slot">The touch slot.</param>
    /// <returns>A completed task.</returns>
    public ValueTask NotifyTouchUpAsync(ObjectPath sessionHandle, Dictionary<string, VariantValue> options, uint slot)
    {
        Injector(sessionHandle, InputDeviceCapability.Touch)?.TouchUp(slot);
        return default;
    }

    /// <summary>
    /// Creates an EIS client connection that injects the granted devices. The first call creates the receiver, with one region per stream or per output.
    /// </summary>
    /// <param name="sessionHandle">A started remote desktop session.</param>
    /// <param name="appId">The application that owns the session.</param>
    /// <param name="options">No options are read.</param>
    /// <returns>The client socket, which the caller owns.</returns>
    /// <exception cref="DBusErrorReplyException">The session has not started, libeis is missing, or no input sink is registered.</exception>
    public ValueTask<SafeHandle> ConnectToEISAsync(ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not RemoteDesktopSession session || !session.IsStarted)
        {
            throw PortalError.Denied("The session has not started.");
        }

        if (!EisLibrary.IsAvailable(out var whyNot))
        {
            throw PortalError.Unsupported($"libeis is not available ({whyNot ?? "not installed"}).");
        }

        if (_sink is null)
        {
            throw PortalError.Unsupported("This compositor does not inject input.");
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
