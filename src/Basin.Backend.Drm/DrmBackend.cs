using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Session;
using Drm;
using Liftoff;
using Udev;
using static Basin.Backend.Drm.DrmLog;

namespace Basin.Backend.Drm;

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
    private IEventSource? _drmSource;
    private UdevContext? _udev;
    private UdevMonitor? _udevMonitor;
    private IEventSource? _udevSource;

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

    public string DevicePath { get; private set; }

    internal ICompositorEventLoop Loop => _loop;

    public string? RenderNodePath { get; private set; }

    public string? DriverName { get; private set; }

    internal bool CursorRidesWithFrame { get; private set; }

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

    public DrmDevice Device { get; private set; } = null!;

    public IReadOnlyList<DrmOutput> Outputs => _outputs;

    public DrmLeaseDevice Leasing { get; }

    public (int Width, int Height) CursorSize { get; private set; } = (64, 64);

    public event Action<DrmOutput>? OutputAdded;

    public event Action<DrmOutput>? OutputRemoved;

    internal DrmFramebufferCache Framebuffers { get; private set; } = null!;

    internal bool SessionActive => _session.IsActive;

    internal LiftoffDevice? Liftoff { get; private set; }

    internal LiftoffDevice? LiftoffProbe { get; private set; }

    private readonly List<(uint PossibleCrtcs, DrmFormatSet Formats)> _overlayPlanes = [];

    internal DrmFormatSet OverlayFormatsFor(int crtcIndex)
    {
        var union = new DrmFormatSet();
        foreach (var (possibleCrtcs, formats) in _overlayPlanes)
        {
            if ((possibleCrtcs & (1u << crtcIndex)) != 0)
            {
                union = union.Union(formats);
            }
        }

        return union;
    }

    public void Start()
    {
        if (DevicePath.Length == 0)
        {
            DevicePath = PickPrimaryDevice() ?? throw new InvalidOperationException(
                "No KMS-capable DRM device found. Is a GPU driver loaded (/dev/dri/card*)?");
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
                $"{DevicePath} does not support atomic modesetting (driver too old or " +
                $"nomodeset in effect): {e.Message}", e);
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
            $"{DevicePath}: driver {DriverName ?? "unknown"}; cursor plane " +
            $"{(CursorRidesWithFrame ? "rides with a frame commit while a layer scans out" : "commits on its own")}");

        InitLiftoff();
        SaveCrtcState();
        RescanConnectors();

        _drmSource = _loop.AddFd(Device.Fd, FdReadiness.Readable, (_, _) => Device.DispatchEvents(_eventHandlers));
        _session.Enabled += OnSessionEnabled;
        _session.Disabled += OnSessionDisabled;
        StartUdevMonitor();
    }

    public void Dispose()
    {
        if (Device is not null)
        {
            Leasing.RevokeAll();
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
        LiftoffProbe?.Dispose();
        LiftoffProbe = null;
        if (Device is not null)
        {
            RestoreCrtcState();
            Framebuffers?.Dispose();
            Device.Dispose();
        }

        _device?.Dispose();
    }

    private static string? PickPrimaryDevice() => DrmDevices.PickPrimary(DrmDevices.Enumerate())?.CardPath;

    private static string? FindRenderNode(string cardPath) =>
        DrmDevices.Enumerate().FirstOrDefault(d => d.CardPath == cardPath)?.RenderNodePath;

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
                catch (DrmException)
                {
                }
            }

            var props = new DrmPropertyMap(Device, connectorId, DrmObjectType.Connector);
            var nonDesktop = props.TryGetValue("non-desktop", out var flag) && flag != 0;
            var currentIndex = nonDesktop
                ? _leaseCrtcs.GetValueOrDefault(connectorId, -1)
                : _outputs.FirstOrDefault(o => o.Name == connector.Name)?.CrtcIndex ?? -1;
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
                _outputs.Remove(output);
                OutputRemoved?.Invoke(output);
                output.Destroy();
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
                Log.Warn($"{connector.Name}: no primary plane reaches CRTC {crtcId}; connector stays dark");
                continue;
            }

            var cursor = FindPlane(crtcIndex, 2 );
            var output = new DrmOutput(this, connector, crtcId, crtcIndex, primary.Value, cursor);
            _outputs.Add(output);
            OutputAdded?.Invoke(output);
        }

        Leasing.SetConnectors(leasable);
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
                Log.Warn($"{connector.Name}: no primary plane reaches CRTC {crtcId}; not offered for leasing");
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
            LiftoffProbe = LiftoffDevice.Create(Device.Fd);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Liftoff?.Dispose();
            Liftoff = null;
            Log.Info($"libliftoff not available; overlay plane offload disabled");
            return;
        }
        catch (LiftoffException e)
        {
            Liftoff?.Dispose();
            Liftoff = null;
            Log.Warn($"{DevicePath}: liftoff device creation failed ({e.Message}); overlay plane offload disabled");
            return;
        }

        LiftoffLog.Priority = LiftoffLogPriority.Debug;
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
                LiftoffProbe!.CreatePlane(planeId);
                overlays++;
                _overlayPlanes.Add((Device.GetPlane(planeId).PossibleCrtcs, DrmOutput.ReadInFormats(Device, props)));
            }
            catch (LiftoffException e)
            {
                Log.Warn($"plane {planeId}: liftoff registration failed ({e.Message})");
            }
        }

        if (overlays == 0)
        {
            Liftoff.Dispose();
            Liftoff = null;
            LiftoffProbe?.Dispose();
            LiftoffProbe = null;
            Log.Info($"{DevicePath}: no overlay planes; plane offload disabled");
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
        catch (DrmException)
        {
        }

        foreach (var output in _outputs)
        {
            output.OnSessionEnabled();
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
            Log.Warn($"udev monitor unavailable ({e.Message}); connector hotplug disabled");
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
            catch (DrmException)
            {
            }
        }
    }

    private void RestoreCrtcState()
    {
        foreach (var (crtcId, bufferId, mode, x, y, connectorId) in _savedCrtcs)
        {
            try
            {
                if (mode is { } m)
                {
                    Device.SetCrtc(crtcId, bufferId, x, y, [connectorId], m);
                }
            }
            catch (DrmException e)
            {
                Log.Warn($"console restore for CRTC {crtcId} failed ({e.Message}); a VT switch will repaint it");
            }
        }
    }
}
