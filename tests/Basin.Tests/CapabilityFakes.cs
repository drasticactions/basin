using Basin.Capabilities;

namespace Basin.Tests;

internal sealed class TestCaptureDmabufConstraints : ICaptureDmabufConstraints
{
    public const ulong Device = 0xE201;

    public DrmFormatSet Formats { get; } = new();

    public bool HasDevice { get; set; } = true;

    public static TestCaptureDmabufConstraints Typical()
    {
        var constraints = new TestCaptureDmabufConstraints();
        constraints.Formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
        constraints.Formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierInvalid);
        constraints.Formats.Add(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear);
        return constraints;
    }

    public bool TryDevice(out ulong device)
    {
        device = Device;
        return HasDevice;
    }
}

internal sealed class TestScreenCapture : IScreenCapture
{
    private readonly CompositorTestHost _host;

    public TestScreenCapture(CompositorTestHost host) => _host = host;

    private readonly CaptureDamageObservers _damageObservers = new();

    public void AddDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Add(observer);

    public void RemoveDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Remove(observer);

    public CaptureSourceKind Refuses { get; set; } = CaptureSourceKind.None;

    public void Damage(IOutput output, Box box) => _damageObservers.Damaged(output, box);

    public bool Supports(in CaptureSource source) =>
        source.Kind != CaptureSourceKind.None && source.Kind != Refuses;

    public bool TryDescribe(in CaptureSource source, out CaptureFormat format)
    {
        format = default;
        if (source.OutputTarget is not { } output || source.Kind == Refuses)
        {
            return false;
        }

        var mode = output.CurrentMode;
        format = new CaptureFormat(mode.Width, mode.Height, DrmFormat.Xrgb8888);
        return true;
    }

    public bool Capture(in CaptureSource source, in Box region, IBuffer target)
    {
        if (source.Kind == Refuses)
        {
            return false;
        }

        var scale = source.OutputTarget?.Scale ?? 1;
        var origin = source.OutputTarget is { } output ? _host.Layout.BoxOf(output) : default;
        _host.Scene.Root.SetPosition(-(origin.X + region.X), -(origin.Y + region.Y));
        try
        {
            return _host.Scene.Render(_host.Renderer, target, RenderColor.Black, scale);
        }
        finally
        {
            _host.Scene.Root.SetPosition(0, 0);
        }
    }

    public CaptureCursorState Cursor { get; private set; }

    public IBuffer? CursorImage { get; private set; }

    public void SetCursor(IBuffer? image, in CaptureCursorState state)
    {
        CursorImage = image;
        Cursor = image is null ? default : state with { IsVisible = true };
        _damageObservers.CursorChanged();
    }

    public bool TryCursorState(IOutput output, out CaptureCursorState cursor)
    {
        cursor = Cursor;
        return Cursor.IsVisible;
    }
}

internal sealed class TestToplevelModel : IToplevelModel
{
    private readonly List<ToplevelInfo> _toplevels = [];

    private readonly ToplevelObservers _observers = new();

    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    public List<(ulong Id, ToplevelRequestKind Kind)> Requests { get; } = [];

    public List<(ulong Id, ToplevelRequest Request)> RequestLog { get; } = [];

    public ulong Add(string title, string appId, Surface? surface = null, Box geometry = default)
    {
        var id = (ulong)_toplevels.Count + 1;
        _toplevels.Add(new ToplevelInfo(id, title, appId, ToplevelState.None, surface, geometry));
        _observers.Added(id);
        return id;
    }

    public void Retitle(ulong id, string title)
    {
        var index = _toplevels.FindIndex(t => t.Id == id);
        if (index >= 0)
        {
            _toplevels[index] = _toplevels[index] with { Title = title };
            _observers.Changed(id);
        }
    }

    public void Reposition(ulong id, Box geometry)
    {
        for (var index = 0; index < _toplevels.Count; index++)
        {
            if (_toplevels[index].Id == id)
            {
                _toplevels[index] = _toplevels[index] with { Geometry = geometry };
                _observers.Changed(id);
                return;
            }
        }
    }

    public void SetIdentity(ulong id, string resourceName = "", uint pid = 0, ulong parentId = 0)
    {
        var index = _toplevels.FindIndex(t => t.Id == id);
        if (index >= 0)
        {
            _toplevels[index] = _toplevels[index] with
            {
                ResourceName = resourceName,
                Pid = pid,
                ParentId = parentId,
            };
            _observers.Changed(id);
        }
    }

    public void SetAppMenu(ulong id, string serviceName, string objectPath)
    {
        var index = _toplevels.FindIndex(t => t.Id == id);
        if (index >= 0)
        {
            _toplevels[index] = _toplevels[index] with
            {
                AppMenuService = serviceName,
                AppMenuObjectPath = objectPath,
            };
            _observers.Changed(id);
        }
    }

    public void SetState(ulong id, ToplevelState state)
    {
        var index = _toplevels.FindIndex(t => t.Id == id);
        if (index >= 0)
        {
            _toplevels[index] = _toplevels[index] with { State = state };
            _observers.Changed(id);
        }
    }

    public void SetClientGeometry(ulong id, Box client)
    {
        var index = _toplevels.FindIndex(t => t.Id == id);
        if (index >= 0)
        {
            _toplevels[index] = _toplevels[index] with { ClientGeometry = client };
            _observers.Changed(id);
        }
    }

    public void Remove(ulong id)
    {
        _toplevels.RemoveAll(t => t.Id == id);
        _observers.Removed(id);
    }

    public int Enumerate(Span<ToplevelInfo> toplevels)
    {
        if (_toplevels.Count > toplevels.Length)
        {
            return -1;
        }

        for (var i = 0; i < _toplevels.Count; i++)
        {
            toplevels[i] = _toplevels[i];
        }

        return _toplevels.Count;
    }

    public bool TryGet(ulong toplevelId, out ToplevelInfo info)
    {
        foreach (var entry in _toplevels)
        {
            if (entry.Id == toplevelId)
            {
                info = entry;
                return true;
            }
        }

        info = default;
        return false;
    }

    public bool Request(ulong toplevelId, in ToplevelRequest request)
    {
        Requests.Add((toplevelId, request.Kind));
        RequestLog.Add((toplevelId, request));
        return true;
    }
}

internal sealed class TestToplevelStack : IToplevelStack
{
    private readonly ToplevelStackObservers _observers = new();
    private readonly List<ulong> _order = [];

    public void AddObserver(IToplevelStackObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IToplevelStackObserver observer) => _observers.Remove(observer);

    public void SetOrder(params ulong[] order)
    {
        _order.Clear();
        _order.AddRange(order);
        _observers.Changed();
    }

    public int Enumerate(Span<ulong> toplevels)
    {
        if (_order.Count > toplevels.Length)
        {
            return -1;
        }

        for (var i = 0; i < _order.Count; i++)
        {
            toplevels[i] = _order[i];
        }

        return _order.Count;
    }
}

internal sealed class RecordingInputSink : IInputSink
{
    public List<(uint Time, uint Key, bool Pressed)> Keys { get; } = [];

    public List<(uint Depressed, uint Latched, uint Locked, uint Group)> ModifierEvents { get; } = [];

    public List<(uint Time, double Dx, double Dy)> Motions { get; } = [];

    public List<(uint Time, double X, double Y, double Width, double Height)> AbsoluteMotions { get; } = [];

    public List<(uint Time, uint Button, bool Pressed)> Buttons { get; } = [];

    public List<(uint Time, uint Axis, double Value)> Axes { get; } = [];

    public List<uint> AxisSources { get; } = [];

    public List<(uint Time, uint Axis)> AxisStops { get; } = [];

    public int Frames { get; private set; }

    public List<byte[]> Keymaps { get; } = [];

    public int CreatedKeyboards { get; private set; }

    private sealed class FakeKeyboard(RecordingInputSink owner) : IInjectedKeyboard
    {
        public object? Tag { get; set; }

        public bool SetKeymap(ReadOnlySpan<byte> keymapText)
        {
            owner.Keymaps.Add(keymapText.ToArray());
            return true;
        }

        public void Dispose()
        {
        }
    }

    public IInjectedKeyboard? CreateKeyboard()
    {
        CreatedKeyboards++;
        return new FakeKeyboard(this);
    }

    public bool Key(IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed)
    {
        Keys.Add((timeMs, keycode, pressed));
        return true;
    }

    public bool Modifiers(IInjectedKeyboard? keyboard, uint depressed, uint latched, uint locked, uint group)
    {
        ModifierEvents.Add((depressed, latched, locked, group));
        return true;
    }

    public bool PointerMotion(uint timeMs, double dx, double dy)
    {
        Motions.Add((timeMs, dx, dy));
        return true;
    }

    public bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height)
    {
        AbsoluteMotions.Add((timeMs, x, y, width, height));
        return true;
    }

    public bool PointerButton(uint timeMs, uint button, bool pressed)
    {
        Buttons.Add((timeMs, button, pressed));
        return true;
    }

    public bool PointerAxis(uint timeMs, uint axis, double value)
    {
        Axes.Add((timeMs, axis, value));
        return true;
    }

    public bool PointerAxisSource(uint source)
    {
        AxisSources.Add(source);
        return true;
    }

    public bool PointerAxisStop(uint timeMs, uint axis)
    {
        AxisStops.Add((timeMs, axis));
        return true;
    }

    public bool Frame()
    {
        Frames++;
        return true;
    }
}

internal sealed class TestDrmLeaseDevice : IDrmLeaseDevice
{
    private readonly List<LeasableConnector> _connectors = [];

    public event Action<uint>? LeaseRevoked;

    public event Action? ConnectorsChanged;

    public List<uint> Revoked { get; } = [];

    public List<int> HandedOut { get; } = [];

    public List<uint[]> LeaseRequests { get; } = [];

    public bool GrantsLeases { get; set; } = true;

    public void Offer(LeasableConnector connector)
    {
        _connectors.Add(connector);
        ConnectorsChanged?.Invoke();
    }

    public void Withdraw(uint connectorId)
    {
        _connectors.RemoveAll(c => c.ConnectorId == connectorId);
        ConnectorsChanged?.Invoke();
    }

    public void EndLease(uint lesseeId) => LeaseRevoked?.Invoke(lesseeId);

    public int OpenEnumerationFd()
    {
        var fd = TestPipe.OpenReadEnd();
        HandedOut.Add(fd);
        return fd;
    }

    public int EnumerateConnectors(Span<LeasableConnector> connectors)
    {
        if (_connectors.Count > connectors.Length)
        {
            return -1;
        }

        for (var i = 0; i < _connectors.Count; i++)
        {
            connectors[i] = _connectors[i];
        }

        return _connectors.Count;
    }

    public bool TryCreateLease(ReadOnlySpan<uint> objectIds, out int leaseFd, out uint lesseeId)
    {
        if (!GrantsLeases)
        {
            leaseFd = -1;
            lesseeId = 0;
            return false;
        }

        LeaseRequests.Add(objectIds.ToArray());
        leaseFd = TestPipe.OpenReadEnd();
        HandedOut.Add(leaseFd);
        lesseeId = 42;
        return true;
    }

    public void RevokeLease(uint lesseeId)
    {
        Revoked.Add(lesseeId);
        LeaseRevoked?.Invoke(lesseeId);
    }
}

internal static class TestPipe
{
    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern unsafe int pipe(int* fds);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    public static unsafe int OpenReadEnd()
    {
        var fds = stackalloc int[2];
        if (pipe(fds) != 0)
        {
            return -1;
        }

        close(fds[1]);
        return fds[0];
    }
}

internal sealed class ToplevelCapture : IScreenCapture
{
    private readonly CompositorTestHost _host;

    public ToplevelCapture(CompositorTestHost host) => _host = host;

    private readonly CaptureDamageObservers _damageObservers = new();

    public void AddDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Add(observer);

    public void RemoveDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Remove(observer);

    public List<ulong> Captured { get; } = [];

    public void Damage(IOutput output, Box box) => _damageObservers.Damaged(output, box);

    public bool Supports(in CaptureSource source) => source.Kind == CaptureSourceKind.Toplevel;

    public bool TryDescribe(in CaptureSource source, out CaptureFormat format)
    {
        format = new CaptureFormat(60, 50, DrmFormat.Xrgb8888);
        return source.Kind == CaptureSourceKind.Toplevel;
    }

    public bool Capture(in CaptureSource source, in Box region, IBuffer target)
    {
        if (source.Kind != CaptureSourceKind.Toplevel)
        {
            return false;
        }

        Captured.Add(source.ToplevelId);
        return _host.Scene.Render(_host.Renderer, target, RenderColor.Black);
    }

    public bool TryCursorState(IOutput output, out CaptureCursorState cursor)
    {
        cursor = default;
        return false;
    }

    public void SetCursor(IBuffer? image, in CaptureCursorState state)
    {
    }
}

internal sealed class TestDmabufCapture : IDmabufCapture
{
    public DmabufAttributes? Frame { get; set; }

    public bool TryCurrentFrame(IOutput output, out DmabufAttributes attributes)
    {
        if (Frame is { } frame)
        {
            attributes = frame;
            return true;
        }

        attributes = default;
        return false;
    }
}

internal sealed class TestOutputPower : IOutputPower
{
    private readonly Dictionary<IOutput, bool> _states = [];

    public event Action<IOutput>? PowerChanged;

    public List<(IOutput Output, bool On)> Requests { get; } = [];

    public bool IsOn(IOutput output) => _states.GetValueOrDefault(output, true);

    public bool SetOn(IOutput output, bool on)
    {
        Requests.Add((output, on));
        _states[output] = on;
        PowerChanged?.Invoke(output);
        return true;
    }
}

internal sealed class TestOutputGamma : IOutputGamma
{
    public List<OutputGammaRamps?> Applied { get; } = [];

    public uint Size { get; set; } = 4;

    public uint RampSize(IOutput output) => Size;

    public bool Apply(IOutput output, in OutputGammaRamps ramps)
    {
        Applied.Add(ramps);
        return true;
    }

    public bool Reset(IOutput output)
    {
        Applied.Add(null);
        return true;
    }
}

internal sealed class TestWorkspaceModel : IWorkspaceModel
{
    private sealed class GroupEntry
    {
        public ulong Id;
        public bool ClientsCanCreateWorkspaces;
        public readonly List<IOutput> Outputs = [];
    }

    private sealed class WorkspaceEntry
    {
        public ulong Id;
        public ulong GroupId;
        public string Name = "";
        public string? Handle;
        public WorkspaceStateFlags State;
        public uint[]? Coordinates;
        public readonly List<WorkspaceMember> Members = [];
    }

    private readonly List<GroupEntry> _groups = [];
    private readonly List<WorkspaceEntry> _workspaces = [];
    private ulong _nextId;

    private readonly WorkspaceObservers _observers = new();

    public void AddObserver(IWorkspaceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IWorkspaceObserver observer) => _observers.Remove(observer);

    public List<(ulong TargetId, WorkspaceRequest Request)> Requests { get; } = [];

    public bool Accept { get; set; } = true;

    public ulong AddGroup(bool clientsCanCreateWorkspaces = true, params IOutput[] outputs)
    {
        var group = new GroupEntry { Id = ++_nextId, ClientsCanCreateWorkspaces = clientsCanCreateWorkspaces };
        group.Outputs.AddRange(outputs);
        _groups.Add(group);
        _observers.Changed();
        return group.Id;
    }

    public ulong AddWorkspace(
        ulong groupId,
        string name,
        string? handle = null,
        WorkspaceStateFlags state = WorkspaceStateFlags.None,
        uint[]? coordinates = null)
    {
        var workspace = new WorkspaceEntry
        {
            Id = ++_nextId,
            GroupId = groupId,
            Name = name,
            Handle = handle,
            State = state,
            Coordinates = coordinates,
        };
        _workspaces.Add(workspace);
        _observers.Changed();
        return workspace.Id;
    }

    public void Rename(ulong id, string name)
    {
        Find(id).Name = name;
        _observers.Changed();
    }

    public void SetState(ulong id, WorkspaceStateFlags state)
    {
        Find(id).State = state;
        _observers.Changed();
    }

    public void SetCoordinates(ulong id, uint[]? coordinates)
    {
        Find(id).Coordinates = coordinates;
        _observers.Changed();
    }

    public void MoveToGroup(ulong id, ulong groupId)
    {
        Find(id).GroupId = groupId;
        _observers.Changed();
    }

    public void RemoveWorkspace(ulong id)
    {
        _workspaces.RemoveAll(w => w.Id == id);
        _observers.Changed();
    }

    public void RemoveGroup(ulong id)
    {
        _workspaces.RemoveAll(w => w.GroupId == id);
        _groups.RemoveAll(g => g.Id == id);
        _observers.Changed();
    }

    public void SetOutputs(ulong groupId, params IOutput[] outputs)
    {
        var group = _groups.Find(g => g.Id == groupId)!;
        group.Outputs.Clear();
        group.Outputs.AddRange(outputs);
        _observers.Changed();
    }

    public void Raise() => _observers.Changed();

    public void SetMembers(ulong workspaceId, params WorkspaceMember[] members)
    {
        var workspace = Find(workspaceId);
        workspace.Members.Clear();
        workspace.Members.AddRange(members);
        _observers.MembersChanged();
    }

    public int EnumerateGroups(Span<WorkspaceGroupInfo> groups)
    {
        if (_groups.Count > groups.Length)
        {
            return -1;
        }

        for (var i = 0; i < _groups.Count; i++)
        {
            groups[i] = new WorkspaceGroupInfo(_groups[i].Id, _groups[i].ClientsCanCreateWorkspaces);
        }

        return _groups.Count;
    }

    public int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces)
    {
        var count = 0;
        foreach (var entry in _workspaces)
        {
            if (entry.GroupId != groupId)
            {
                continue;
            }

            if (count == workspaces.Length)
            {
                return -1;
            }

            workspaces[count++] = new WorkspaceInfo(entry.Id, entry.Name, entry.Handle, entry.State, entry.Coordinates);
        }

        return count;
    }

    public int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs)
    {
        var group = _groups.Find(g => g.Id == groupId);
        if (group is null)
        {
            return 0;
        }

        if (group.Outputs.Count > outputs.Length)
        {
            return -1;
        }

        for (var i = 0; i < group.Outputs.Count; i++)
        {
            outputs[i] = group.Outputs[i];
        }

        return group.Outputs.Count;
    }

    public int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members)
    {
        var workspace = _workspaces.Find(w => w.Id == workspaceId);
        if (workspace is null)
        {
            return 0;
        }

        if (workspace.Members.Count > members.Length)
        {
            return -1;
        }

        for (var i = 0; i < workspace.Members.Count; i++)
        {
            members[i] = workspace.Members[i];
        }

        return workspace.Members.Count;
    }

    public bool Request(ulong targetId, in WorkspaceRequest request)
    {
        Requests.Add((targetId, request));
        return Accept;
    }

    private WorkspaceEntry Find(ulong id) => _workspaces.Find(w => w.Id == id)!;
}

internal sealed class TestPreferenceOutput : OutputBase
{
    public TestPreferenceOutput(string name = "PREF-1")
        : base(name)
    {
        using var initial = new OutputState();
        Commit(initial.SetEnabled(true).SetMode(new OutputMode(640, 480, 60_000)));
    }

    public OutputRgbRange CommittedRgbRange { get; private set; }

    public uint CommittedMaxBpc { get; private set; }

    public uint CommittedOverscan { get; private set; }

    protected override bool SupportsAdaptiveSync => true;

    protected override bool SupportsRgbRange => true;

    protected override bool SupportsMaxBitsPerColor => true;

    protected override bool SupportsOverscan => true;

    protected override bool SupportsCustomModes => true;

    protected override bool SupportsSharpness => true;

    protected override bool SupportsAbmLevel => true;

    public override OutputConfigurationFeatures Features =>
        OutputConfigurationFeatures.Overscan |
        OutputConfigurationFeatures.Vrr |
        OutputConfigurationFeatures.RgbRange |
        OutputConfigurationFeatures.MaxBitsPerColor |
        OutputConfigurationFeatures.CustomModes |
        OutputConfigurationFeatures.Sharpness |
        OutputConfigurationFeatures.AbmLevel;

    public IReadOnlyList<OutputMode>? CommittedCustomModes { get; private set; }

    public uint CommittedSharpness { get; private set; }

    public uint CommittedAbmLevel { get; private set; }

    protected override bool TestCommitCore(OutputState state) => true;

    protected override bool CommitCore(OutputState state)
    {
        if ((state.Fields & OutputStateFields.CustomModes) != 0)
        {
            CommittedCustomModes = state.CustomModes;
        }

        if ((state.Fields & OutputStateFields.Sharpness) != 0)
        {
            CommittedSharpness = state.Sharpness;
        }

        if ((state.Fields & OutputStateFields.AbmLevel) != 0)
        {
            CommittedAbmLevel = state.AbmLevel;
        }

        if ((state.Fields & OutputStateFields.RgbRange) != 0)
        {
            CommittedRgbRange = state.RgbRange;
        }

        if ((state.Fields & OutputStateFields.MaxBitsPerColor) != 0)
        {
            CommittedMaxBpc = state.MaxBitsPerColor;
        }

        if ((state.Fields & OutputStateFields.Overscan) != 0)
        {
            CommittedOverscan = state.Overscan;
        }

        return true;
    }
}

internal sealed class TestHdrOutput : OutputBase, IOutputColorPipeline
{
    public uint DegammaLutSize => 256;

    public uint GammaLutSize => 256;

    public bool SupportsCtm => true;

    public OutputGammaRamps? CommittedGamma { get; private set; }

    public bool GammaFieldSeen { get; private set; }

    public OutputGammaRamps? CommittedDegamma { get; private set; }

    public bool DegammaFieldSeen { get; private set; }

    public TestHdrOutput(string name = "HDR-1")
        : base(name)
    {
        using var initial = new OutputState();
        Commit(initial.SetEnabled(true).SetMode(new OutputMode(640, 480, 60_000)));
    }

    public HdrStaticMetadata? CommittedHdr { get; private set; }

    public bool HdrFieldSeen { get; private set; }

    public double[]? CommittedCtm { get; private set; }

    public bool CtmFieldSeen { get; private set; }

    public override OutputConfigurationFeatures Features =>
        OutputConfigurationFeatures.HighDynamicRange |
        OutputConfigurationFeatures.WideColorGamut |
        OutputConfigurationFeatures.IccProfile |
        OutputConfigurationFeatures.HdrIccProfile |
        OutputConfigurationFeatures.BuiltInColor;

    public override OutputColorimetry? Colorimetry => new OutputColorimetry
    {
        MaxLuminance = 600,
        MaxFrameAverageLuminance = 400,
        MinLuminance = 0.05,
        Chromaticities = (0.68, 0.32, 0.265, 0.69, 0.15, 0.06, 0.3127, 0.3290),
        SupportsPq = true,
        SupportsBt2020 = true,
    };

    protected override bool TestCommitCore(OutputState state) => true;

    protected override bool CommitCore(OutputState state)
    {
        if ((state.Fields & OutputStateFields.Hdr) != 0)
        {
            CommittedHdr = state.Hdr;
            HdrFieldSeen = true;
        }

        if ((state.Fields & OutputStateFields.Ctm) != 0)
        {
            CommittedCtm = state.Ctm;
            CtmFieldSeen = true;
        }

        if ((state.Fields & OutputStateFields.GammaLut) != 0)
        {
            CommittedGamma = state.GammaLut;
            GammaFieldSeen = true;
        }

        if ((state.Fields & OutputStateFields.DegammaLut) != 0)
        {
            CommittedDegamma = state.DegammaLut;
            DegammaFieldSeen = true;
        }

        return true;
    }
}

internal sealed class TestEdidOutput : OutputBase
{
    private readonly byte[] _edid;

    public TestEdidOutput(string name, byte[] edid)
        : base(name)
    {
        _edid = edid;
        using var initial = new OutputState();
        Commit(initial.SetEnabled(true).SetMode(new OutputMode(640, 480, 60_000)));
    }

    public override ReadOnlyMemory<byte> EdidBytes => _edid;

    protected override bool TestCommitCore(OutputState state) => true;

    protected override bool CommitCore(OutputState state) => true;
}

internal sealed class TestScreencastPublisher : IScreencastPublisher
{
    public List<ScreencastRequest> Requests { get; } = [];

    public List<ulong> ClosedStreams { get; } = [];

    public bool TryPublish(in ScreencastRequest request, out ScreencastStreamInfo info)
    {
        Requests.Add(request);
        info = new ScreencastStreamInfo { NodeId = 77 };
        return true;
    }

    public void Close(ulong streamId) => ClosedStreams.Add(streamId);
}
