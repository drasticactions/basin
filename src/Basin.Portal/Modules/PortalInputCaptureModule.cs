using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Eis;
using Basin.Portal.DBus;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class PortalInputCaptureModule : PortalModule, IInputCaptureHandler, IInputCaptureProperties
{
    public const uint CapabilityKeyboard = 1;

    public const uint CapabilityPointer = 2;

    public const uint CapabilityTouchscreen = 4;

    private IPortalPrompts _prompts = null!;
    private IInputCaptureProvider _provider = null!;
    private OutputLayout _layout = null!;
    private PortalOptions _options = new();

    public override string WireInterface => "org.freedesktop.impl.portal.InputCapture";

    public override int Version => 2;

    public override IReadOnlyList<Type> Capabilities => [typeof(IAppInfoResolver)];

    public override IReadOnlyList<Type> Drivers => [typeof(IPortalPrompts), typeof(IInputCaptureProvider), typeof(OutputLayout)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalInputCapture;

    public uint SupportedCapabilities => CapabilityKeyboard | CapabilityPointer;

    uint IInputCaptureProperties.Version => (uint)Version;

    public Func<(double X, double Y)?>? CursorPositionSource { get; set; }

    internal (double X, double Y)? CursorPosition => CursorPositionSource?.Invoke();

    public override bool ShouldInstall(BasinServices services)
    {
        if (!base.ShouldInstall(services))
        {
            return false;
        }

        if (EisLibrary.IsAvailable(out var whyNot))
        {
            return true;
        }

        Log.Info($"{WireInterface} not offered: {whyNot}");
        return false;
    }

    protected override void Bind(BasinServices services)
    {
        _prompts = services.Require<IPortalPrompts>();
        _layout = services.Require<OutputLayout>();
        _provider = services.Require<IInputCaptureProvider>();
        _options = services.Find<PortalOptions>() ?? new PortalOptions();
    }

    ValueTask IInputCaptureHandler.HandleGetPropertyAsync(IInputCaptureHandler.GetPropertyContext context) => context.Handle(this);

    ValueTask IInputCaptureHandler.HandleGetAllPropertiesAsync(IInputCaptureHandler.GetAllPropertiesContext context) => context.Handle(this);

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, string parentWindow, Dictionary<string, VariantValue> options)
    {
        var session = Create(sessionHandle, appId);
        var requested = ToCapability(Vardict.UInt32(options, "capabilities", SupportedCapabilities)) & ToCapability(SupportedCapabilities);
        session.Requested = requested;
        session.Granted = requested;
        session.IsStarted = true;
        session.Engine = _provider.Open(session, session.Id);
        if (session.Engine is null)
        {
            session.Close();
            return ValueTask.FromResult((PortalResponse.Other, Results.Empty));
        }

        return ValueTask.FromResult((PortalResponse.Success, Results.With("capabilities", VariantValue.UInt32(ToTypes(requested)))));
    }

    public ValueTask<Dictionary<string, VariantValue>> CreateSession2Async(ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        _ = Create(sessionHandle, appId);
        return ValueTask.FromResult(Results.Empty);
    }

    public async ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> StartAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, string parentWindow, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not InputCaptureSession session || session.IsStarted)
        {
            return (PortalResponse.Other, Results.Empty);
        }

        var request = TrackRequest(handle);
        try
        {
            if (!Vardict.Has(options, "capabilities"))
            {
                throw PortalError.Invalid("Start needs a capabilities option");
            }

            var requested = ToCapability(Vardict.UInt32(options, "capabilities")) & ToCapability(SupportedCapabilities);
            if (requested == 0)
            {
                throw PortalError.Invalid("no supported capability was requested");
            }

            session.Requested = requested;
            session.PersistMode = Math.Min(Vardict.UInt32(options, "persist_mode"), 2);
            session.RestoreCandidate = null;
            if (Vardict.RestoreData(options, "restore_data") is { } restore &&
                string.Equals(restore.Vendor, _options.RestoreVendor, StringComparison.Ordinal) && restore.Version == RestoreData.Version &&
                restore.Data.Type == VariantValueType.Dictionary &&
                restore.Data.GetDictionary<string, VariantValue>().TryGetValue("capabilities", out var stored) && stored.Type == VariantValueType.UInt32)
            {
                session.RestoreCandidate = ToCapability(stored.GetUInt32());
            }

            var modal = Vardict.Bool(options, "modal", true);
            InputDeviceCapability granted;
            bool clipboard;
            uint persist;
            if (session.RestoreCandidate is { } candidate && candidate != 0 && (candidate & ~requested) == 0)
            {
                granted = candidate;
                clipboard = session.Clipboard.Requested;
                persist = session.PersistMode;
            }
            else
            {
                var body = session.Clipboard.Requested
                    ? $"{Describe(appId)} wants to capture input when the pointer leaves the screen, and share the clipboard"
                    : $"{Describe(appId)} wants to capture input when the pointer leaves the screen";
                var answer = await _prompts.Confirm(Named(new ConfirmPrompt(appId, parentWindow, modal, "Allow input capture?", body)), request.Token).ConfigureAwait(true);
                if (!answer.IsAccepted || !answer.Value || session.IsClosed)
                {
                    return (PortalScreenshotModule.ResponseFor(answer.Response, request), Results.Empty);
                }

                granted = requested;
                clipboard = session.Clipboard.Requested;
                persist = session.PersistMode;
            }

            session.Granted = granted;
            session.Clipboard.Enabled = clipboard;
            session.IsStarted = true;
            session.Engine = _provider.Open(session, session.Id);
            if (session.Engine is null)
            {
                Log.Warn($"input capture for {appId}: the compositor refused a capture session");
                session.Close();
                return (PortalResponse.Other, Results.Empty);
            }

            var results = new Dictionary<string, VariantValue>
            {
                ["capabilities"] = VariantValue.UInt32(ToTypes(granted)),
                ["clipboard_enabled"] = VariantValue.Bool(clipboard),
            };
            if (persist != 0)
            {
                results["persist_mode"] = VariantValue.UInt32(persist);
                var data = new Dictionary<string, VariantValue> { ["capabilities"] = VariantValue.UInt32(ToTypes(granted)) };
                results["restore_data"] = Results.Restore(_options.RestoreVendor, RestoreData.Version, Results.Dict(data));
            }

            Log.Info($"input capture for {appId}: started with {granted}");
            return (PortalResponse.Success, results);
        }
        finally
        {
            ReleaseRequest(request);
        }
    }

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> GetZonesAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not InputCaptureSession)
        {
            return ValueTask.FromResult((PortalResponse.Other, Results.Empty));
        }

        var zones = new Array<Struct<uint, uint, int, int>>();
        foreach (var (output, _) in _layout.Outputs)
        {
            var box = _layout.BoxOf(output);
            zones.Add(Struct.Create((uint)Math.Max(0, box.Width), (uint)Math.Max(0, box.Height), box.X, box.Y));
        }

        var results = new Dictionary<string, VariantValue>
        {
            ["zones"] = zones.AsVariantValue(),
            ["zone_set"] = VariantValue.UInt32(_provider.ZoneSet),
        };
        return ValueTask.FromResult((PortalResponse.Success, results));
    }

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> SetPointerBarriersAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options,
        Dictionary<string, VariantValue>[] barriers, uint zoneSet)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not InputCaptureSession { Engine: { } engine })
        {
            return ValueTask.FromResult((PortalResponse.Other, Results.Empty));
        }

        var failed = new List<uint>();
        engine.ClearBarriers();
        var stale = zoneSet != _provider.ZoneSet;
        foreach (var properties in barriers)
        {
            var id = Vardict.UInt32(properties, "barrier_id");
            var position = properties.TryGetValue("position", out var value) && value.Type == VariantValueType.Struct && value.Count == 4
                ? (value.GetItem(0).GetInt32(), value.GetItem(1).GetInt32(), value.GetItem(2).GetInt32(), value.GetItem(3).GetInt32())
                : (0, 0, 0, 0);
            var barrier = new InputCaptureBarrier(id, position.Item1, position.Item2, position.Item3, position.Item4);
            if (id == 0 || stale || !InputCaptureBarriers.IsValid(in barrier, _layout) || !engine.TryAddBarrier(in barrier))
            {
                failed.Add(id);
            }
        }

        if (stale)
        {
            Log.Info($"input capture for {appId}: barriers for zone set {zoneSet} refused, the current set is {_provider.ZoneSet}");
        }

        return ValueTask.FromResult((PortalResponse.Success, Results.With("failed_barriers", VariantValue.Array(failed.ToArray()))));
    }

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> EnableAsync(ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is InputCaptureSession { Engine: { } engine })
        {
            engine.Enable();
        }

        return ValueTask.FromResult((PortalResponse.Success, Results.Empty));
    }

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> DisableAsync(ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is InputCaptureSession { Engine: { } engine })
        {
            engine.Disable();
        }

        return ValueTask.FromResult((PortalResponse.Success, Results.Empty));
    }

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> ReleaseAsync(ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is InputCaptureSession { Engine: { } engine })
        {
            var activation = Vardict.UInt32(options, "activation_id", engine.ActivationId);
            var (x, y) = (-1.0, -1.0);
            if (options.TryGetValue("cursor_position", out var position) && position.Type == VariantValueType.Struct && position.Count == 2)
            {
                x = position.GetItem(0).GetDouble();
                y = position.GetItem(1).GetDouble();
            }

            if (!engine.Release(activation, x, y))
            {
                Log.Info($"input capture for {appId}: release for activation {activation} ignored, the current one is {engine.ActivationId}");
            }
        }

        return ValueTask.FromResult((PortalResponse.Success, Results.Empty));
    }

    public ValueTask<SafeHandle> ConnectToEISAsync(ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not InputCaptureSession { Engine: { } engine })
        {
            throw PortalError.Denied("the session has not started");
        }

        var fd = engine.TakeEisFd();
        if (fd < 0)
        {
            throw PortalError.Unsupported("the compositor handed out no EIS connection for this session");
        }

        Log.Info($"input capture for {appId}: eis client connected");
        return ValueTask.FromResult<SafeHandle>(new SafeFileHandle(fd, ownsHandle: true));
    }

    internal void Release(InputCaptureSession session)
    {
        session.Clipboard.Detach();
        session.Engine?.Dispose();
        session.Engine = null;
    }

    private InputCaptureSession Create(ObjectPath sessionHandle, string appId)
    {
        var bus = Bus ?? throw new InvalidOperationException("the portal bus is not connected");
        var session = new InputCaptureSession(this, bus, sessionHandle, appId);
        bus.Register(session);
        return session;
    }

    private static InputDeviceCapability ToCapability(uint types)
    {
        var capability = InputDeviceCapability.None;
        if ((types & CapabilityKeyboard) != 0)
        {
            capability |= InputDeviceCapability.Keyboard;
        }

        if ((types & CapabilityPointer) != 0)
        {
            capability |= InputDeviceCapability.Pointer;
        }

        if ((types & CapabilityTouchscreen) != 0)
        {
            capability |= InputDeviceCapability.Touch;
        }

        return capability;
    }

    private static uint ToTypes(InputDeviceCapability capability)
    {
        var types = 0u;
        if ((capability & InputDeviceCapability.Keyboard) != 0)
        {
            types |= CapabilityKeyboard;
        }

        if ((capability & InputDeviceCapability.Pointer) != 0)
        {
            types |= CapabilityPointer;
        }

        if ((capability & InputDeviceCapability.Touch) != 0)
        {
            types |= CapabilityTouchscreen;
        }

        return types;
    }
}
