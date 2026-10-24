using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Session;
using Drm;
using Drm.Native;
using Liftoff;
using Udev;
using static Basin.Backend.Drm.DrmLog;

namespace Basin.Backend.Drm;

/// <summary>
/// The KMS backend for one DRM card. It opens the card through the session, makes one <see cref="DrmOutput"/> per connected connector with a CRTC, and follows hotplug.
/// </summary>
public sealed class DrmBackend : IDisposable
{
    private readonly ICompositorEventLoop _loop;
    private readonly ISession _session;
    private readonly List<DrmOutput> _outputs = [];
    private readonly DrmEventHandlers _eventHandlers;
    private readonly List<(uint CrtcId, uint BufferId, DrmModeInfo? Mode, uint X, uint Y, uint ConnectorId)> _savedCrtcs = [];

    private readonly Dictionary<uint, int> _leaseCrtcs = [];
    private readonly HashSet<uint> _leasePlanes = [];

    private ISessionDevice? _device;
    private bool _scanned;
    private IEventSource? _drmSource;
    private UdevContext? _udev;
    private UdevMonitor? _udevMonitor;
    private IEventSource? _udevSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrmBackend"/> class. It touches no device until <see cref="Start"/>.
    /// </summary>
    /// <param name="loop">The loop that receives the DRM and udev fds.</param>
    /// <param name="session">The session that opens the card and reports VT switches.</param>
    /// <param name="devicePath">The card path, for example <c>/dev/dri/card0</c>, or <see langword="null"/> to pick the primary card at start.</param>
    public DrmBackend(ICompositorEventLoop loop, ISession session, string? devicePath = null)
    {
        _loop = loop;
        _session = session;
        DevicePath = devicePath ?? string.Empty;
        Leasing = new DrmLeaseDevice(this);
        _eventHandlers = new DrmEventHandlers
        {
            PageFlip = OnPageFlip,
        };
    }

    /// <summary>
    /// Gets the card path. It is empty before <see cref="Start"/> when the constructor received no path.
    /// </summary>
    public string DevicePath { get; private set; }

    internal ICompositorEventLoop Loop => _loop;

    /// <summary>
    /// Gets the render node of the card. It is <see langword="null"/> before <see cref="Start"/> or when the card has none.
    /// </summary>
    public string? RenderNodePath { get; private set; }

    /// <summary>
    /// Gets the kernel driver name of the card, for example <c>amdgpu</c>. It is <see langword="null"/> before <see cref="Start"/> or when the kernel refuses the query.
    /// </summary>
    public string? DriverName { get; private set; }

    internal bool CursorRidesWithFrame { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the card accepts asynchronous page flips in atomic commits. It is <see langword="false"/> before <see cref="Start"/>.
    /// </summary>
    public bool SupportsAtomicAsync { get; private set; }

    private string? ReadDriverName()
    {
        try
        {
            return Device.GetVersion().Name;
        }
        catch (DrmException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the opened DRM device. It is not valid before <see cref="Start"/>.
    /// </summary>
    public DrmDevice Device { get; private set; } = null!;

    /// <summary>
    /// Gets the outputs that exist now, one per connected desktop connector that has a CRTC.
    /// </summary>
    public IReadOnlyList<DrmOutput> Outputs => _outputs;

    /// <summary>
    /// Gets the DRM lease device that offers the card's non-desktop connectors to clients.
    /// </summary>
    public DrmLeaseDevice Leasing { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the first scan keeps a lit connector on its CRTC and records its state for a handoff. It must be set before <see cref="Start"/>.
    /// </summary>
    public bool Inherit { get; set; } = true;

    /// <summary>
    /// Gets or sets what <see cref="Dispose"/> leaves on the screen. The default is <see cref="DrmExitMode.Restore"/>.
    /// </summary>
    public DrmExitMode ExitMode { get; set; }

    /// <summary>
    /// Occurs in <see cref="Dispose"/> once per output when <see cref="ExitMode"/> is <see cref="DrmExitMode.Retain"/>, with the result of the retention.
    /// </summary>
    public event Action<HandoffRetention>? HandoffRetained;

    internal bool Retiring { get; private set; }

    internal Func<int, uint, int> CloseFramebuffer { get; set; } = static (fd, fbId) => -Libdrm.drmModeCloseFB(fd, fbId);

    /// <summary>
    /// Gets the largest cursor buffer that the card accepts, in pixels. It is 64 by 64 when the card reports no size.
    /// </summary>
    public (int Width, int Height) CursorSize { get; private set; } = (64, 64);

    /// <summary>
    /// Occurs when a scan creates an output, for example at start or after a hotplug.
    /// </summary>
    public event Action<DrmOutput>? OutputAdded;

    /// <summary>
    /// Occurs before the backend destroys an output, when its connector is gone or its CRTC changed.
    /// </summary>
    public event Action<DrmOutput>? OutputRemoved;

    internal DrmFramebufferCache Framebuffers { get; private set; } = null!;

    internal bool SessionActive => _session.IsActive;

    internal LiftoffDevice? Liftoff { get; private set; }

    private readonly List<(uint PossibleCrtcs, DrmFormatSet Formats, uint Encodings, uint Ranges)> _overlayPlanes = [];

    internal static readonly string[] EncodingNames = ["ITU-R BT.601 YCbCr", "ITU-R BT.709 YCbCr", "ITU-R BT.2020 YCbCr"];

    internal static readonly string[] RangeNames = ["YCbCr limited range", "YCbCr full range"];

    internal bool OverlayTakesYuv(int crtcIndex, YuvCoefficients coefficients, YuvRange range)
    {
        foreach (var plane in _overlayPlanes)
        {
            if ((plane.PossibleCrtcs & (1u << crtcIndex)) != 0 &&
                (plane.Encodings & (1u << (int)coefficients)) != 0 &&
                (plane.Ranges & (1u << (int)range)) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private uint EnumMask(DrmPropertyMap props, string property, string[] names)
    {
        if (!props.Has(property))
        {
            return 0;
        }

        var mask = 0u;
        var info = Device.GetProperty(props.IdOf(property));
        foreach (var entry in info.EnumEntries)
        {
            var index = Array.IndexOf(names, entry.Name);
            if (index >= 0)
            {
                mask |= 1u << index;
            }
        }

        return mask;
    }

    internal DrmFormatSet OverlayFormatsFor(int crtcIndex)
    {
        var union = new DrmFormatSet();
        foreach (var (possibleCrtcs, formats, _, _) in _overlayPlanes)
        {
            if ((possibleCrtcs & (1u << crtcIndex)) != 0)
            {
                union = union.Union(formats);
            }
        }

        return union;
    }

    /// <summary>
    /// Opens the card through the session, enables atomic modesetting, scans the connectors and starts the hotplug monitor.
    /// </summary>
    /// <exception cref="InvalidOperationException">No KMS card is present, or the card does not support atomic modesetting.</exception>
    public void Start()
    {
        if (DevicePath.Length == 0)
        {
            DevicePath = PickPrimaryDevice() ?? throw new InvalidOperationException(
                "No DRM device with modesetting support is present. Load a GPU driver that provides /dev/dri/card*.");
        }

        _device = _session.OpenDevice(DevicePath);
        Device = DrmDevice.FromFd(_device.FileDescriptor, ownsFd: false);
        try
        {
            Device.SetClientCapability(DrmClientCapability.UniversalPlanes, 1);
            Device.SetClientCapability(DrmClientCapability.Atomic, 1);
        }
        catch (DrmException e)
        {
            throw new InvalidOperationException(
                $"{DevicePath} does not support atomic modesetting. Use a newer driver, and remove nomodeset from the kernel command line.", e);
        }

        if (Device.TryGetCapability(DrmCapability.CursorWidth, out var cursorWidth) &&
            Device.TryGetCapability(DrmCapability.CursorHeight, out var cursorHeight) &&
            cursorWidth > 0 && cursorHeight > 0)
        {
            CursorSize = ((int)cursorWidth, (int)cursorHeight);
        }
        else
        {
            Log.Debug($"{DevicePath}: no cursor size reported, using {CursorSize.Width}x{CursorSize.Height}");
        }

        Framebuffers = new DrmFramebufferCache(Device) { IsScanningOut = IsBufferOnScreen };
        RenderNodePath = FindRenderNode(DevicePath);
        DriverName = ReadDriverName();
        CursorRidesWithFrame = DriverName is "nvidia-drm" or "nvidia";
        Log.Debug(
            $"{DevicePath}: driver {DriverName ?? "unknown"}, cursor plane " +
            $"{(CursorRidesWithFrame ? "rides with a frame commit while a layer scans out" : "commits on its own")}");

        SupportsAtomicAsync = Device.TryGetCapability(DrmCapability.AtomicAsyncPageFlip, out var atomicAsync) && atomicAsync != 0;
        Log.Debug($"{DevicePath}: atomic async page flips {(SupportsAtomicAsync ? "supported" : "not supported")}");
        InitLiftoff();
        SaveCrtcState();
        UnlinkForeignPlanes();
        RescanConnectors();

        _drmSource = _loop.AddFd(Device.Fd, FdReadiness.Readable, (_, _) => Device.DispatchEvents(_eventHandlers));
        _session.Enabled += OnSessionEnabled;
        _session.Disabled += OnSessionDisabled;
        StartUdevMonitor();
    }

    /// <summary>
    /// Revokes the leases, retains or turns off each output as <see cref="ExitMode"/> says, restores the saved CRTCs that it did not retain and closes the card.
    /// </summary>
    public void Dispose()
    {
        if (Device is not null)
        {
            Leasing.RevokeAll();
        }

        var retained = new HashSet<uint>();
        if (Device is not null && ExitMode == DrmExitMode.Retain)
        {
            Retiring = true;
            DrainFlips(100);
            foreach (var output in _outputs)
            {
                var retention = output.Retain();
                if (retention.Retained)
                {
                    retained.Add(output.CrtcId);
                }

                HandoffRetained?.Invoke(retention);
            }
        }

        foreach (var output in _outputs.ToArray())
        {
            output.Destroy();
        }

        _outputs.Clear();
        _udevSource?.Remove();
        _udevMonitor?.Dispose();
        _udev?.Dispose();
        _drmSource?.Remove();
        Liftoff?.Dispose();
        Liftoff = null;
        if (Device is not null)
        {
            RestoreCrtcState(retained);
            Framebuffers?.Dispose();
            Device.Dispose();
        }

        _device?.Dispose();
    }

    private static string? PickPrimaryDevice() => DrmDevices.PickPrimary(DrmDevices.Enumerate())?.CardPath;

    private static string? FindRenderNode(string cardPath) =>
        DrmDevices.Enumerate().FirstOrDefault(d => d.CardPath == cardPath)?.RenderNodePath;

    /// <summary>
    /// Reads the connectors again and reassigns CRTCs. It raises <see cref="OutputRemoved"/> and <see cref="OutputAdded"/> for the changes and updates the leasable connectors.
    /// </summary>
    public void RescanConnectors()
    {
        var resources = Device.GetResources();
        var crtcIds = resources.CrtcIds;

        var connected = new List<(DrmConnector Connector, uint Mask, int CurrentIndex, bool NonDesktop)>();
        foreach (var connectorId in resources.ConnectorIds)
        {
            DrmConnector connector;
            try
            {
                connector = Device.GetConnector(connectorId);
            }
            catch (DrmException)
            {
                continue;
            }

            if (connector.Status != DrmConnectionStatus.Connected || connector.Type == DrmConnectorType.Writeback)
            {
                continue;
            }

            uint mask = 0;
            foreach (var encoderId in connector.EncoderIds)
            {
                try
                {
                    mask |= Device.GetEncoder(encoderId).PossibleCrtcs;
                }
                catch (DrmException e)
                {
                    Log.Debug(e, $"{connector.Name}: encoder {encoderId} does not read, skipped");
                }
            }

            var props = new DrmPropertyMap(Device, connectorId, DrmObjectType.Connector);
            var nonDesktop = props.TryGetValue("non-desktop", out var flag) && flag != 0;
            var currentIndex = nonDesktop
                ? _leaseCrtcs.GetValueOrDefault(connectorId, -1)
                : _outputs.FirstOrDefault(o => o.Name == connector.Name)?.CrtcIndex ?? -1;
            if (currentIndex < 0 && !nonDesktop && !_scanned && Inherit)
            {
                currentIndex = LitCrtcIndex(connector, crtcIds);
            }

            connected.Add((connector, mask, currentIndex, nonDesktop));
        }

        var candidates = new CrtcCandidate[connected.Count];
        for (var i = 0; i < connected.Count; i++)
        {
            candidates[i] = new CrtcCandidate(connected[i].Mask, connected[i].CurrentIndex);
        }

        var assignment = CrtcAssignment.Solve(candidates, crtcIds.Count);

        var keep = new HashSet<string>();
        for (var i = 0; i < connected.Count; i++)
        {
            if (!connected[i].NonDesktop && assignment[i] >= 0 && assignment[i] == connected[i].CurrentIndex)
            {
                keep.Add(connected[i].Connector.Name);
            }
        }

        foreach (var output in _outputs.ToArray())
        {
            if (!keep.Contains(output.Name))
            {
                var still = -1;
                for (var i = 0; i < connected.Count; i++)
                {
                    if (connected[i].Connector.Name == output.Name)
                    {
                        still = i;
                    }
                }

                if (still < 0)
                {
                    Log.Debug($"{output.Name}: connector disconnected, output removed");
                }
                else
                {
                    Log.Debug($"{output.Name}: CRTC changed from {connected[still].CurrentIndex} to {assignment[still]}, output rebuilt");
                }

                _outputs.Remove(output);
                OutputRemoved?.Invoke(output);
                output.Destroy();
            }
        }

        foreach (var (connector, _, _, nonDesktop) in connected)
        {
            if (!nonDesktop && _outputs.FirstOrDefault(o => o.Name == connector.Name) is { } kept)
            {
                kept.Refresh(connector);
                kept.CheckLinkStatus();
            }
        }

        var leasable = ScanLeasable(connected, assignment, crtcIds);

        for (var i = 0; i < connected.Count; i++)
        {
            var (connector, _, currentIndex, nonDesktop) = connected[i];
            if (nonDesktop || assignment[i] < 0 ||
                (assignment[i] == currentIndex && _outputs.Any(o => o.Name == connector.Name)))
            {
                continue;
            }

            var crtcIndex = assignment[i];
            var crtcId = crtcIds[crtcIndex];
            var primary = FindPlane(crtcIndex, 1 );
            if (primary is null)
            {
                Log.Warn($"{connector.Name}: no primary plane reaches CRTC {crtcId}, connector stays dark");
                continue;
            }

            var cursor = FindPlane(crtcIndex, 2 );
            var output = new DrmOutput(this, connector, crtcId, crtcIndex, primary.Value, cursor);
            if (!_scanned && LitCrtcIndex(connector, crtcIds) == crtcIndex &&
                ReadInherited(connector, crtcId) is { } inherited)
            {
                Log.Debug($"{connector.Name}: inherits {inherited.OutputMode.Width}x{inherited.OutputMode.Height} on CRTC {crtcId} with framebuffer {inherited.FramebufferId}");
                output.Inherit(inherited);
            }
            else if (!_scanned)
            {
                Log.Debug($"{connector.Name}: nothing lit to inherit (lit CRTC index {LitCrtcIndex(connector, crtcIds)}, assigned {crtcIndex})");
            }

            _outputs.Add(output);
            OutputAdded?.Invoke(output);
        }

        Leasing.SetConnectors(leasable);
        _scanned = true;
    }

    private int LitCrtcIndex(DrmConnector connector, IReadOnlyList<uint> crtcIds)
    {
        if (connector.CurrentEncoderId == 0)
        {
            return -1;
        }

        try
        {
            var crtcId = Device.GetEncoder(connector.CurrentEncoderId).CrtcId;
            if (crtcId == 0 || Device.GetCrtc(crtcId).Mode is null)
            {
                return -1;
            }

            for (var i = 0; i < crtcIds.Count; i++)
            {
                if (crtcIds[i] == crtcId)
                {
                    return i;
                }
            }
        }
        catch (DrmException e)
        {
            Log.Debug(e, $"{connector.Name}: lit CRTC does not read, nothing to inherit");
        }

        return -1;
    }

    private DrmInheritedState? ReadInherited(DrmConnector connector, uint crtcId)
    {
        try
        {
            var crtc = Device.GetCrtc(crtcId);
            if (crtc.Mode is not { } mode)
            {
                return null;
            }

            var crtcProps = new DrmPropertyMap(Device, crtcId, DrmObjectType.Crtc);
            if (crtcProps.TryGetValue("ACTIVE", out var active) && active == 0)
            {
                return null;
            }

            ulong sourceWidth = 0, sourceHeight = 0;
            uint crtcWidth = 0, crtcHeight = 0;
            foreach (var planeId in Device.GetPlaneIds())
            {
                var plane = Device.GetPlane(planeId);
                if (plane.CrtcId != crtcId || plane.FramebufferId != crtc.BufferId || crtc.BufferId == 0)
                {
                    continue;
                }

                var planeProps = new DrmPropertyMap(Device, planeId, DrmObjectType.Plane);
                sourceWidth = planeProps.TryGetValue("SRC_W", out var srcW) ? srcW : 0;
                sourceHeight = planeProps.TryGetValue("SRC_H", out var srcH) ? srcH : 0;
                crtcWidth = planeProps.TryGetValue("CRTC_W", out var dstW) ? (uint)dstW : 0;
                crtcHeight = planeProps.TryGetValue("CRTC_H", out var dstH) ? (uint)dstH : 0;
                break;
            }

            var props = new DrmPropertyMap(Device, connector.ConnectorId, DrmObjectType.Connector);
            var hdr = props.TryGetValue("HDR_OUTPUT_METADATA", out var hdrBlob) && hdrBlob != 0;
            byte eotf = 0;
            if (hdr)
            {
                try
                {
                    var data = Device.GetPropertyBlob((uint)hdrBlob).Data.Span;
                    eotf = data.Length > 4 ? data[4] : (byte)0;
                }
                catch (DrmException e)
                {
                    Log.Debug(e, $"{connector.Name}: HDR metadata blob does not read, EOTF taken as SDR");
                }
            }

            return new DrmInheritedState(
                mode,
                DrmOutput.ToOutputMode(mode),
                crtc.BufferId,
                sourceWidth,
                sourceHeight,
                crtcWidth,
                crtcHeight,
                props.TryGetValue("Colorspace", out var colorspace) ? colorspace : null,
                hdr,
                eotf,
                props.TryGetValue("max bpc", out var maxBpc) ? maxBpc : null,
                crtcProps.TryGetValue("GAMMA_LUT", out var gamma) && gamma != 0,
                crtcProps.TryGetValue("DEGAMMA_LUT", out var degamma) && degamma != 0,
                crtcProps.TryGetValue("CTM", out var ctm) && ctm != 0);
        }
        catch (DrmException)
        {
            return null;
        }
    }

    private List<LeasableConnector> ScanLeasable(
        List<(DrmConnector Connector, uint Mask, int CurrentIndex, bool NonDesktop)> connected,
        int[] assignment,
        IReadOnlyList<uint> crtcIds)
    {
        _leaseCrtcs.Clear();
        _leasePlanes.Clear();
        var leasable = new List<LeasableConnector>();
        for (var i = 0; i < connected.Count; i++)
        {
            var (connector, _, _, nonDesktop) = connected[i];
            if (!nonDesktop || assignment[i] < 0)
            {
                continue;
            }

            var crtcIndex = assignment[i];
            var crtcId = crtcIds[crtcIndex];
            var primary = FindPlane(crtcIndex, 1 );
            if (primary is null)
            {
                Log.Warn($"{connector.Name}: no primary plane reaches CRTC {crtcId}, connector not offered for leasing");
                continue;
            }

            _leasePlanes.Add(primary.Value);
            var cursor = FindPlane(crtcIndex, 2 );
            if (cursor is { } cursorPlane)
            {
                _leasePlanes.Add(cursorPlane);
            }

            _leaseCrtcs[connector.ConnectorId] = crtcIndex;
            var edid = DrmOutput.ReadEdid(Device, new DrmPropertyMap(Device, connector.ConnectorId, DrmObjectType.Connector));
            leasable.Add(new LeasableConnector(
                connector.Name,
                $"{edid.Make} {edid.Model} ({connector.Name})",
                connector.ConnectorId,
                cursor is { } withCursor
                    ? [connector.ConnectorId, crtcId, primary.Value, withCursor]
                    : [connector.ConnectorId, crtcId, primary.Value]));
        }

        return leasable;
    }

    private void InitLiftoff()
    {
        try
        {
            Liftoff = LiftoffDevice.Create(Device.Fd);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Liftoff?.Dispose();
            Liftoff = null;
            Log.Info($"liftoff: libliftoff not found, overlay plane offload disabled");
            return;
        }
        catch (LiftoffException e)
        {
            Liftoff?.Dispose();
            Liftoff = null;
            Log.Warn(e, $"{DevicePath}: liftoff device creation failed, overlay plane offload disabled");
            return;
        }

        LiftoffLog.Priority = Log.IsEnabled(BasinLogLevel.Debug) ? LiftoffLogPriority.Debug : LiftoffLogPriority.Error;
        LiftoffLog.SetHandler(static (priority, message) =>
        {
            if (priority == LiftoffLogPriority.Error)
            {
                Log.Warn($"liftoff: {message}");
            }
            else
            {
                Log.Debug($"liftoff: {message}");
            }
        });

        var overlays = 0;
        foreach (var planeId in Device.GetPlaneIds())
        {
            var props = new DrmPropertyMap(Device, planeId, DrmObjectType.Plane);
            if (!props.TryGetValue("type", out var type) || type != 0 )
            {
                continue;
            }

            try
            {
                Liftoff.CreatePlane(planeId);
                overlays++;
                _overlayPlanes.Add((
                    Device.GetPlane(planeId).PossibleCrtcs,
                    DrmOutput.ReadInFormats(Device, props),
                    EnumMask(props, "COLOR_ENCODING", EncodingNames),
                    EnumMask(props, "COLOR_RANGE", RangeNames)));
            }
            catch (LiftoffException e)
            {
                Log.Warn(e, $"{DevicePath}: liftoff registration failed for plane {planeId}");
            }
        }

        if (overlays == 0)
        {
            Liftoff.Dispose();
            Liftoff = null;
            Log.Info($"{DevicePath}: no overlay planes, plane offload disabled");
            return;
        }

        Log.Info($"{DevicePath}: libliftoff managing {overlays} overlay plane(s)");
    }

    private uint? FindPlane(int crtcIndex, ulong planeType)
    {
        foreach (var planeId in Device.GetPlaneIds())
        {
            var plane = Device.GetPlane(planeId);
            if ((plane.PossibleCrtcs & (1u << crtcIndex)) == 0)
            {
                continue;
            }

            if (_leasePlanes.Contains(planeId) ||
                _outputs.Any(o => o.PlaneId == planeId || o.CursorPlaneId == planeId))
            {
                continue;
            }

            var props = new DrmPropertyMap(Device, planeId, DrmObjectType.Plane);
            if (props.TryGetValue("type", out var type) && type == planeType)
            {
                return planeId;
            }
        }

        return null;
    }

    private bool IsBufferOnScreen(IBuffer buffer)
    {
        foreach (var output in _outputs)
        {
            if (output.IsScanningOut(buffer))
            {
                return true;
            }
        }

        return false;
    }

    private void OnPageFlip(uint sequence, uint seconds, uint microseconds, uint crtcId, nint userData)
    {
        var id = crtcId != 0 ? crtcId : (uint)userData;
        foreach (var output in _outputs)
        {
            if (output.CrtcId == id)
            {
                output.OnPageFlip(sequence, seconds, microseconds);
                return;
            }
        }
    }

    private void OnSessionEnabled()
    {
        try
        {
            Device.SetMaster();
        }
        catch (DrmException e)
        {
            Log.Debug(e, $"{DevicePath}: SET_MASTER refused, the session keeps master");
        }

        UnlinkForeignPlanes();
        foreach (var output in _outputs)
        {
            output.OnSessionEnabled();
        }
    }

    internal void UnlinkForeignPlanes()
    {
        using var request = new DrmAtomicBuilder();
        var any = false;
        foreach (var planeId in Device.GetPlaneIds())
        {
            DrmPlane plane;
            try
            {
                plane = Device.GetPlane(planeId);
            }
            catch (DrmException)
            {
                continue;
            }

            if (plane.CrtcId == 0)
            {
                continue;
            }

            bool hasMode;
            try
            {
                hasMode = Device.GetCrtc(plane.CrtcId).Mode is not null;
            }
            catch (DrmException)
            {
                hasMode = true;
            }

            if (hasMode)
            {
                continue;
            }

            var props = new DrmPropertyMap(Device, planeId, DrmObjectType.Plane);
            if (!props.Has("FB_ID") || !props.Has("CRTC_ID"))
            {
                continue;
            }

            Log.Debug($"{DevicePath}: unlinking plane {planeId} left on CRTC {plane.CrtcId}, which has no mode");
            request.Add(props, "plane", "FB_ID", 0);
            request.Add(props, "plane", "CRTC_ID", 0);
            any = true;
        }

        if (!any)
        {
            return;
        }

        try
        {
            Device.AtomicCommit(request.Request, DrmAtomicCommitFlags.AllowModeset, 0);
        }
        catch (DrmException e) when (e.Errno == 13)
        {
            Log.Debug($"{DevicePath}: foreign planes not unlinked, the device is not DRM master (EACCES)");
        }
        catch (DrmException e)
        {
            Log.Warn(e, $"{DevicePath}: unlinking foreign planes failed, staged properties:\n{request}");
        }
    }

    private void OnSessionDisabled()
    {
        Leasing.RevokeAll();
        foreach (var output in _outputs)
        {
            output.OnSessionDisabled();
        }
    }

    private void StartUdevMonitor()
    {
        try
        {
            _udev = new UdevContext();
            _udevMonitor = _udev.CreateMonitor(UdevMonitorSource.Udev);
            _udevMonitor.FilterMatchSubsystemDevtype("drm", null!);
            _udevMonitor.EnableReceiving();
            _udevSource = _loop.AddFd(_udevMonitor.Fd, FdReadiness.Readable, (_, _) => DrainUdev());
        }
        catch (UdevException e)
        {
            Log.Warn(e, $"{DevicePath}: udev monitor unavailable, connector hotplug disabled");
        }
    }

    private void DrainUdev()
    {
        var rescan = false;
        while (_udevMonitor!.TryReceiveDevice() is { } device)
        {
            using (device)
            {
                if (device.Action == "change" && device.Devnode == DevicePath)
                {
                    rescan = true;
                }
            }
        }

        if (rescan && SessionActive)
        {
            RescanConnectors();
        }
    }

    private unsafe bool FramebufferExists(uint fbId)
    {
        var fb = Libdrm.drmModeGetFB2(Device.Fd, fbId);
        if (fb is null)
        {
            return false;
        }

        Span<uint> closed = stackalloc uint[4];
        var count = 0;
        for (var plane = 0; plane < 4; plane++)
        {
            var handle = fb->handles[plane];
            if (handle != 0 && !closed[..count].Contains(handle))
            {
                closed[count++] = handle;
                Libdrm.drmCloseBufferHandle(Device.Fd, handle);
            }
        }

        Libdrm.drmModeFreeFB2(fb);
        return true;
    }

    private unsafe void DrainFlips(int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (_outputs.Any(o => o.FlipPending))
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                Log.Debug($"{DevicePath}: a flip was still pending {timeoutMs} ms into the exit");
                return;
            }

            var pollFd = new PollFd { Fd = Device.Fd, Events = PollIn };
            if (poll(&pollFd, 1, (int)remaining) > 0 && (pollFd.Revents & PollIn) != 0)
            {
                Device.DispatchEvents(_eventHandlers);
            }
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    private const short PollIn = 1;

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern unsafe int poll(PollFd* fds, nuint nfds, int timeoutMs);

    private void SaveCrtcState()
    {
        var resources = Device.GetResources();
        foreach (var connectorId in resources.ConnectorIds)
        {
            try
            {
                var connector = Device.GetConnector(connectorId);
                if (connector.Status != DrmConnectionStatus.Connected || connector.CurrentEncoderId == 0)
                {
                    continue;
                }

                var encoder = Device.GetEncoder(connector.CurrentEncoderId);
                if (encoder.CrtcId == 0)
                {
                    continue;
                }

                var crtc = Device.GetCrtc(encoder.CrtcId);
                if (crtc.BufferId != 0)
                {
                    _savedCrtcs.Add((crtc.CrtcId, crtc.BufferId, crtc.Mode, crtc.X, crtc.Y, connectorId));
                }
            }
            catch (DrmException e)
            {
                Log.Debug(e, $"{DevicePath}: console state of connector {connectorId} does not read, not restored at exit");
            }
        }
    }

    private void RestoreCrtcState(HashSet<uint> retained)
    {
        foreach (var (crtcId, bufferId, mode, x, y, connectorId) in _savedCrtcs)
        {
            if (retained.Contains(crtcId) || !SessionActive)
            {
                continue;
            }

            try
            {
                if (Framebuffers.Owns(bufferId) || !FramebufferExists(bufferId))
                {
                    Log.Debug($"{DevicePath}: console framebuffer {bufferId} for CRTC {crtcId} is gone or reused, CRTC left off");
                    Device.SetCrtc(crtcId, 0, 0, 0, [], null);
                }
                else if (mode is { } m)
                {
                    Device.SetCrtc(crtcId, bufferId, x, y, [connectorId], m);
                }
            }
            catch (DrmException e)
            {
                Log.Warn(e, $"{DevicePath}: console restore for CRTC {crtcId} failed, a VT switch repaints it");
            }
        }
    }
}
