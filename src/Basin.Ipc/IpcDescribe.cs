using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcDescribe
{
    private readonly BasinServices _services;
    private IpcOutput[] _outputScratch = [];
    private IpcWindow[] _windowScratch = [];
    private IpcWorkspaceGroup[] _groupScratch = [];
    private IpcWorkspace[] _workspaceScratch = [];
    private string[] _nameScratch = [];
    private ulong[] _memberScratch = [];

    public IpcDescribe(BasinServices services)
    {
        _services = services;
        Toplevels = services.Find<IToplevelModel>();
        Stack = services.Find<IToplevelStack>();
        Workspaces = services.Find<IWorkspaceModel>();
        Layout = services.Find<OutputLayout>();
        Outputs = services.Find<IOutputSet>();
        Power = services.Find<IOutputPower>();
        Configuration = services.Find<IOutputConfiguration>();
        Capture = services.Find<IScreenCapture>();
    }

    public IToplevelModel? Toplevels { get; }

    public IToplevelStack? Stack { get; }

    public IWorkspaceModel? Workspaces { get; }

    public OutputLayout? Layout { get; }

    public IOutputSet? Outputs { get; }

    public IOutputPower? Power { get; }

    public IOutputConfiguration? Configuration { get; }

    public IScreenCapture? Capture { get; }

    public Func<(double X, double Y)?>? PointerPosition { get; set; }

    public IReadOnlyList<IOutput> AllOutputs()
    {
        if (Outputs is { } set)
        {
            return set.Outputs;
        }

        if (Layout is { } layout)
        {
            var list = new List<IOutput>();
            foreach (var (output, _) in layout.Outputs)
            {
                list.Add(output);
            }

            return list;
        }

        return [];
    }

    public IOutput? OutputNamed(string name)
    {
        foreach (var output in AllOutputs())
        {
            if (output.Name == name)
            {
                return output;
            }
        }

        return null;
    }

    public IOutput? PointerOutput()
    {
        if (PointerPosition?.Invoke() is { } position && Layout is { } layout)
        {
            return layout.OutputAt(position.X, position.Y);
        }

        if (Capture is { } capture)
        {
            foreach (var output in AllOutputs())
            {
                if (capture.TryCursorState(output, out var cursor) && cursor.IsVisible)
                {
                    return output;
                }
            }
        }

        return null;
    }

    public IpcOutputList OutputList()
    {
        var pointer = PointerOutput();
        var outputs = AllOutputs();
        var list = Grow(ref _outputScratch, outputs.Count);
        for (var i = 0; i < outputs.Count; i++)
        {
            list[i] = Output(outputs[i], pointer);
        }

        return new IpcOutputList(new ReadOnlyMemory<IpcOutput>(list, 0, outputs.Count), pointer?.Name);
    }

    public IpcOutput Output(IOutput output, IOutput? pointer)
    {
        var mode = output.CurrentMode;
        return new IpcOutput(
            output.Name,
            output.Description,
            output.Make,
            output.Model,
            output.Serial,
            output.Enabled,
            new IpcOutputMode(mode.Width, mode.Height, mode.RefreshMilliHz),
            mode.RefreshMilliHz / 1000.0,
            output.Scale,
            IpcWrite.Transform(output.Transform),
            output.AdaptiveSync,
            new IpcSize(output.PhysicalSize.Width, output.PhysicalSize.Height),
            Layout is { } layout && layout.Contains(output) ? IpcWrite.Box(layout.BoxOf(output)) : default(IpcOptionalBox),
            Power is { } power ? power.IsOn(output) : null,
            ReferenceEquals(output, pointer));
    }

    public ToplevelInfo[] Windows() =>
        Toplevels is { } model ? IpcSpans.Fill<ToplevelInfo>(model.Enumerate) : [];

    public Dictionary<ulong, ulong> WorkspaceOf() => WorkspaceOf(new Dictionary<ulong, ulong>());

    public Dictionary<ulong, ulong> WorkspaceOf(Dictionary<ulong, ulong> map)
    {
        map.Clear();
        if (Workspaces is not { } model)
        {
            return map;
        }

        foreach (var group in IpcSpans.Fill<WorkspaceGroupInfo>(model.EnumerateGroups))
        {
            foreach (var workspace in IpcSpans.Fill<WorkspaceInfo>(span => model.EnumerateWorkspaces(group.Id, span)))
            {
                foreach (var member in IpcSpans.Fill<WorkspaceMember>(span => model.EnumerateMembers(workspace.Id, span)))
                {
                    map.TryAdd(member.ToplevelId, workspace.Id);
                }
            }
        }

        return map;
    }

    public IpcWindow Window(in ToplevelInfo info, Dictionary<ulong, ulong>? workspaceOf)
    {
        var geometry = info.Geometry;
        var output = Layout?.OutputAt(geometry.X + (geometry.Width / 2.0), geometry.Y + (geometry.Height / 2.0));
        return new IpcWindow(
            info.Id,
            info.Title,
            info.AppId,
            IpcWrite.States(info.State),
            IpcWrite.Box(info.Geometry),
            IpcWrite.Box(info.ClientGeometry),
            info.Pid,
            info.ParentId == 0 ? null : info.ParentId,
            output?.Name,
            workspaceOf is not null && workspaceOf.TryGetValue(info.Id, out var workspace) ? workspace : null);
    }

    public IpcWindowList WindowList()
    {
        var workspaceOf = WorkspaceOf();
        var windows = Windows();
        var list = Grow(ref _windowScratch, windows.Length);
        for (var i = 0; i < windows.Length; i++)
        {
            list[i] = Window(windows[i], workspaceOf);
        }

        return new IpcWindowList(new ReadOnlyMemory<IpcWindow>(list, 0, windows.Length));
    }

    public IpcStack StackIds() =>
        new(Stack is { } stack ? IpcSpans.Fill<ulong>(stack.Enumerate, 64) : ReadOnlyMemory<ulong>.Empty);

    public IpcWorkspaceList WorkspaceList()
    {
        if (Workspaces is not { } model)
        {
            return new IpcWorkspaceList(ReadOnlyMemory<IpcWorkspaceGroup>.Empty);
        }

        var groups = IpcSpans.Fill<WorkspaceGroupInfo>(model.EnumerateGroups);
        var groupList = Grow(ref _groupScratch, groups.Length);
        var nameAt = 0;
        var workspaceAt = 0;
        var memberAt = 0;
        for (var g = 0; g < groups.Length; g++)
        {
            var group = groups[g];
            var outputs = IpcSpans.Fill<IOutput>(span => model.EnumerateGroupOutputs(group.Id, span), 4);
            var names = Grow(ref _nameScratch, nameAt + outputs.Length);
            for (var o = 0; o < outputs.Length; o++)
            {
                names[nameAt + o] = outputs[o].Name;
            }

            var infos = IpcSpans.Fill<WorkspaceInfo>(span => model.EnumerateWorkspaces(group.Id, span));
            var workspaces = Grow(ref _workspaceScratch, workspaceAt + infos.Length);
            for (var w = 0; w < infos.Length; w++)
            {
                var workspace = infos[w];
                var members = IpcSpans.Fill<WorkspaceMember>(span => model.EnumerateMembers(workspace.Id, span));
                var ids = Grow(ref _memberScratch, memberAt + members.Length);
                for (var m = 0; m < members.Length; m++)
                {
                    ids[memberAt + m] = members[m].ToplevelId;
                }

                workspaces[workspaceAt + w] = new IpcWorkspace(
                    workspace.Id,
                    workspace.Name,
                    (workspace.State & WorkspaceStateFlags.Active) != 0,
                    (workspace.State & WorkspaceStateFlags.Urgent) != 0,
                    (workspace.State & WorkspaceStateFlags.Hidden) != 0,
                    workspace.Coordinates,
                    new ReadOnlyMemory<ulong>(ids, memberAt, members.Length));
                memberAt += members.Length;
            }

            groupList[g] = new IpcWorkspaceGroup(
                group.Id,
                group.ClientsCanCreateWorkspaces,
                new ReadOnlyMemory<string>(names, nameAt, outputs.Length),
                new ReadOnlyMemory<IpcWorkspace>(workspaces, workspaceAt, infos.Length));
            nameAt += outputs.Length;
            workspaceAt += infos.Length;
        }

        return new IpcWorkspaceList(new ReadOnlyMemory<IpcWorkspaceGroup>(groupList, 0, groups.Length));
    }

    private static T[] Grow<T>(ref T[] array, int size)
    {
        if (array.Length < size)
        {
            Array.Resize(ref array, Math.Max(size, array.Length * 2));
        }

        return array;
    }

    public T? Find<T>()
        where T : class => _services.Find<T>();
}
