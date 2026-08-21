using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class WorkspaceManager : IWorkspaceObserver, IDisposable
{
    public const int Version = 1;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal _global;
    private readonly List<Binding> _bindings = [];
    private readonly List<WorkspaceGroup> _groups = [];
    private readonly List<Workspace> _workspaces = [];

    internal sealed class Binding
    {
        public required ExtWorkspaceManagerV1Resource Manager;
        public required Dictionary<WorkspaceGroup, ExtWorkspaceGroupHandleV1Resource> Groups;
        public required Dictionary<Workspace, ExtWorkspaceHandleV1Resource> Workspaces;
        public required List<Action> Pending;
        public bool Stopped;
    }

    private readonly IWorkspaceModel? _model;
    private readonly Dictionary<ulong, WorkspaceGroup> _modelGroups = [];
    private readonly Dictionary<WorkspaceGroup, ulong> _modelGroupIds = [];
    private readonly Dictionary<ulong, Workspace> _modelWorkspaces = [];

    public WorkspaceManager(WlServerDisplay display, IWorkspaceModel? model = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        _model = model;
        _global = display.CreateGlobal(ExtWorkspaceManagerV1.Interface, Version, OnBind);
        if (_model is { } live)
        {
            live.AddObserver(this);
            Reproject();
        }
    }

    public void OnWorkspacesChanged() => Reproject();

    public void OnWorkspaceMembersChanged()
    {
    }

    public void Dispose()
    {
        if (_model is { } live)
        {
            live.RemoveObserver(this);
        }

        foreach (var (output, handler) in _watchedOutputs)
        {
            output.ResourceBound -= handler;
        }

        _watchedOutputs.Clear();
        _global.Dispose();
    }

    private readonly Dictionary<OutputGlobal, Action<WlOutputResource>> _watchedOutputs = [];

    internal void WatchOutput(OutputGlobal output)
    {
        if (_watchedOutputs.ContainsKey(output))
        {
            return;
        }

        void Handler(WlOutputResource resource) => OnOutputResourceBound(output, resource);
        _watchedOutputs[output] = Handler;
        output.ResourceBound += Handler;
    }

    internal void UnwatchOutput(OutputGlobal output)
    {
        foreach (var group in _groups)
        {
            if (group.Outputs.Contains(output))
            {
                return;
            }
        }

        if (_watchedOutputs.Remove(output, out var handler))
        {
            output.ResourceBound -= handler;
        }
    }

    private void OnOutputResourceBound(OutputGlobal output, WlOutputResource resource)
    {
        Broadcast(binding =>
        {
            if (binding.Manager.Client != resource.Client)
            {
                return;
            }

            var sent = false;
            foreach (var group in _groups)
            {
                if (group.Outputs.Contains(output) && binding.Groups.TryGetValue(group, out var handle))
                {
                    handle.SendOutputEnter(resource);
                    sent = true;
                }
            }

            if (sent)
            {
                binding.Manager.SendDone();
            }
        });
    }

    private void Reproject()
    {
        if (_model is not { } model)
        {
            return;
        }

        var groups = new WorkspaceGroupInfo[Math.Max(1, _modelGroups.Count + 8)];
        var groupCount = model.EnumerateGroups(groups);
        while (groupCount < 0)
        {
            groups = new WorkspaceGroupInfo[groups.Length * 2];
            groupCount = model.EnumerateGroups(groups);
        }

        var seenGroups = new HashSet<ulong>();
        var seenWorkspaces = new HashSet<ulong>();
        var workspaces = new WorkspaceInfo[16];
        var outputs = new IOutput[8];

        for (var i = 0; i < groupCount; i++)
        {
            var info = groups[i];
            seenGroups.Add(info.Id);
            if (!_modelGroups.TryGetValue(info.Id, out var group))
            {
                group = CreateGroup(info.ClientsCanCreateWorkspaces);
                _modelGroups[info.Id] = group;
                _modelGroupIds[group] = info.Id;
                var groupId = info.Id;
                group.CreateWorkspaceRequested += name =>
                    model.Request(groupId, new WorkspaceRequest(WorkspaceRequestKind.Create, name));
            }

            var outputCount = model.EnumerateGroupOutputs(info.Id, outputs);
            while (outputCount < 0)
            {
                outputs = new IOutput[outputs.Length * 2];
                outputCount = model.EnumerateGroupOutputs(info.Id, outputs);
            }

            SyncOutputs(group, outputs.AsSpan(0, outputCount));

            var count = model.EnumerateWorkspaces(info.Id, workspaces);
            while (count < 0)
            {
                workspaces = new WorkspaceInfo[workspaces.Length * 2];
                count = model.EnumerateWorkspaces(info.Id, workspaces);
            }

            for (var w = 0; w < count; w++)
            {
                var entry = workspaces[w];
                seenWorkspaces.Add(entry.Id);
                if (_modelWorkspaces.TryGetValue(entry.Id, out var workspace))
                {
                    workspace.SetName(entry.Name);
                    workspace.SetState((WorkspaceState)entry.State);
                    if (workspace.Group != group)
                    {
                        workspace.AssignTo(group);
                    }

                    if (entry.Coordinates is not null && !SameCoordinates(workspace.Coordinates, entry.Coordinates))
                    {
                        workspace.SetCoordinates(entry.Coordinates);
                    }

                    continue;
                }

                workspace = group.CreateWorkspace(entry.Name, entry.Handle, (WorkspaceState)entry.State, entry.Coordinates);
                _modelWorkspaces[entry.Id] = workspace;
                var id = entry.Id;
                workspace.ActivateRequested += () => model.Request(id, new WorkspaceRequest(WorkspaceRequestKind.Activate));
                workspace.DeactivateRequested += () => model.Request(id, new WorkspaceRequest(WorkspaceRequestKind.Deactivate));
                workspace.RemoveRequested += () => model.Request(id, new WorkspaceRequest(WorkspaceRequestKind.Remove));
                workspace.AssignRequested += target =>
                {
                    if (_modelGroupIds.TryGetValue(target, out var targetGroupId))
                    {
                        model.Request(id, new WorkspaceRequest(WorkspaceRequestKind.Assign, GroupId: targetGroupId));
                    }
                };
            }
        }

        foreach (var (id, workspace) in _modelWorkspaces.ToList())
        {
            if (!seenWorkspaces.Contains(id))
            {
                workspace.Remove();
                _modelWorkspaces.Remove(id);
            }
        }

        foreach (var (id, group) in _modelGroups.ToList())
        {
            if (!seenGroups.Contains(id))
            {
                group.Remove();
                _modelGroups.Remove(id);
                _modelGroupIds.Remove(group);
            }
        }
    }

    private static void SyncOutputs(WorkspaceGroup group, ReadOnlySpan<IOutput> outputs)
    {
        var desired = new List<OutputGlobal>(outputs.Length);
        foreach (var output in outputs)
        {
            if (OutputGlobal.For(output) is { } global)
            {
                desired.Add(global);
            }
        }

        for (var i = group.Outputs.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(group.Outputs[i]))
            {
                group.RemoveOutput(group.Outputs[i]);
            }
        }

        foreach (var global in desired)
        {
            if (!group.Outputs.Contains(global))
            {
                group.AddOutput(global);
            }
        }
    }

    private static bool SameCoordinates(uint[]? current, uint[]? next) =>
        current is null ? next is null : next is not null && current.AsSpan().SequenceEqual(next);

    public WorkspaceGroup CreateGroup(bool clientsCanCreateWorkspaces = false)
    {
        var group = new WorkspaceGroup(this, clientsCanCreateWorkspaces);
        _groups.Add(group);
        foreach (var binding in Live())
        {
            AnnounceGroup(binding, group);
            binding.Manager.SendDone();
        }

        return group;
    }

    private IEnumerable<Binding> Live()
    {
        for (var i = _bindings.Count - 1; i >= 0; i--)
        {
            var binding = _bindings[i];
            if (!binding.Stopped && !binding.Manager.IsDestroyed)
            {
                yield return binding;
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ExtWorkspaceManagerV1Resource(client, version, id);
        var binding = new Binding { Manager = manager, Groups = [], Workspaces = [], Pending = [] };
        _bindings.Add(binding);
        manager.Destroyed += (_, _) => _bindings.Remove(binding);
        manager.Stop += (_, _) =>
        {
            binding.Stopped = true;
            manager.SendFinished();
        };
        manager.Commit += (_, _) =>
        {
            foreach (var request in binding.Pending)
            {
                request();
            }

            binding.Pending.Clear();
        };

        foreach (var group in _groups)
        {
            AnnounceGroup(binding, group);
        }

        foreach (var workspace in _workspaces)
        {
            AnnounceWorkspace(binding, workspace);
        }

        manager.SendDone();
    }

    private void AnnounceGroup(Binding binding, WorkspaceGroup group)
    {
        var handle = new ExtWorkspaceGroupHandleV1Resource(binding.Manager.Client, binding.Manager.Version, 0);
        binding.Manager.SendWorkspaceGroup(handle);
        binding.Groups[group] = handle;
        handle.Destroyed += (_, _) => binding.Groups.Remove(group);
        handle.CreateWorkspace += (_, e) =>
        {
            var name = e.Workspace;
            binding.Pending.Add(() => group.RaiseCreateRequested(name));
        };

        handle.SendCapabilities(group.ClientsCanCreateWorkspaces
            ? ExtWorkspaceGroupHandleV1.GroupCapabilities.CreateWorkspace
            : 0);
        foreach (var output in group.Outputs)
        {
            foreach (var resource in output.ResourcesOf(binding.Manager.Client))
            {
                handle.SendOutputEnter(resource);
            }
        }

        foreach (var workspace in _workspaces)
        {
            if (workspace.Group == group && binding.Workspaces.TryGetValue(workspace, out var workspaceHandle))
            {
                handle.SendWorkspaceEnter(workspaceHandle);
            }
        }
    }

    private void AnnounceWorkspace(Binding binding, Workspace workspace)
    {
        var handle = new ExtWorkspaceHandleV1Resource(binding.Manager.Client, binding.Manager.Version, 0);
        binding.Manager.SendWorkspace(handle);
        binding.Workspaces[workspace] = handle;
        handle.Destroyed += (_, _) => binding.Workspaces.Remove(workspace);

        handle.Activate += (_, _) => binding.Pending.Add(workspace.RaiseActivateRequested);
        handle.Deactivate += (_, _) => binding.Pending.Add(workspace.RaiseDeactivateRequested);
        handle.Remove += (_, _) => binding.Pending.Add(workspace.RaiseRemoveRequested);
        handle.Assign += (_, e) =>
        {
            foreach (var (group, groupHandle) in binding.Groups)
            {
                if (groupHandle.RawHandle == e.WorkspaceGroupHandle)
                {
                    binding.Pending.Add(() => workspace.RaiseAssignRequested(group));
                    break;
                }
            }
        };

        if (workspace.Id is { } workspaceId)
        {
            handle.SendId(workspaceId);
        }

        handle.SendName(workspace.Name);
        if (workspace.Coordinates is { } coordinates)
        {
            handle.SendCoordinates(MemoryMarshal.AsBytes(coordinates.AsSpan()));
        }

        handle.SendCapabilities(
            ExtWorkspaceHandleV1.WorkspaceCapabilities.Activate |
            ExtWorkspaceHandleV1.WorkspaceCapabilities.Deactivate |
            ExtWorkspaceHandleV1.WorkspaceCapabilities.Remove |
            ExtWorkspaceHandleV1.WorkspaceCapabilities.Assign);
        handle.SendState((ExtWorkspaceHandleV1.State)workspace.State);

        if (workspace.Group is { } group && binding.Groups.TryGetValue(group, out var owner))
        {
            owner.SendWorkspaceEnter(handle);
        }
    }

    internal void OnWorkspaceAdded(Workspace workspace)
    {
        _workspaces.Add(workspace);
        foreach (var binding in Live())
        {
            AnnounceWorkspace(binding, workspace);
            binding.Manager.SendDone();
        }
    }

    internal void Broadcast(Action<Binding> send)
    {
        foreach (var binding in Live())
        {
            send(binding);
            binding.Manager.SendDone();
        }
    }

    public sealed class WorkspaceGroup
    {
        private readonly WorkspaceManager _owner;

        internal WorkspaceGroup(WorkspaceManager owner, bool clientsCanCreateWorkspaces)
        {
            _owner = owner;
            ClientsCanCreateWorkspaces = clientsCanCreateWorkspaces;
        }

        public bool ClientsCanCreateWorkspaces { get; }

        internal List<OutputGlobal> Outputs { get; } = [];

        public event Action<string>? CreateWorkspaceRequested;

        public void AddOutput(OutputGlobal output)
        {
            Outputs.Add(output);
            _owner.WatchOutput(output);
            _owner.Broadcast(binding =>
            {
                if (binding.Groups.TryGetValue(this, out var handle))
                {
                    foreach (var resource in output.ResourcesOf(binding.Manager.Client))
                    {
                        handle.SendOutputEnter(resource);
                    }
                }
            });
        }

        public void RemoveOutput(OutputGlobal output)
        {
            Outputs.Remove(output);
            _owner.Broadcast(binding =>
            {
                if (binding.Groups.TryGetValue(this, out var handle))
                {
                    foreach (var resource in output.ResourcesOf(binding.Manager.Client))
                    {
                        handle.SendOutputLeave(resource);
                    }
                }
            });
            _owner.UnwatchOutput(output);
        }

        public Workspace CreateWorkspace(string name, string? id = null, WorkspaceState state = 0, uint[]? coordinates = null)
        {
            var workspace = new Workspace(_owner, this, name, id, state, coordinates);
            _owner.OnWorkspaceAdded(workspace);
            return workspace;
        }

        public void Remove()
        {
            _owner._groups.Remove(this);
            _owner.Broadcast(binding =>
            {
                if (binding.Groups.TryGetValue(this, out var handle))
                {
                    handle.SendRemoved();
                }
            });
        }

        internal void RaiseCreateRequested(string name) => CreateWorkspaceRequested?.Invoke(name);
    }

    [Flags]
    public enum WorkspaceState : uint
    {
        Active = 1,
        Urgent = 2,
        Hidden = 4,
    }

    public sealed class Workspace
    {
        private readonly WorkspaceManager _owner;

        internal Workspace(WorkspaceManager owner, WorkspaceGroup group, string name, string? id, WorkspaceState state, uint[]? coordinates)
        {
            _owner = owner;
            Group = group;
            Name = name;
            Id = id;
            State = state;
            Coordinates = coordinates;
        }

        public WorkspaceGroup? Group { get; private set; }

        public string Name { get; private set; }

        public string? Id { get; }

        public WorkspaceState State { get; private set; }

        public uint[]? Coordinates { get; private set; }

        public event Action? ActivateRequested;

        public event Action? DeactivateRequested;

        public event Action? RemoveRequested;

        public event Action<WorkspaceGroup>? AssignRequested;

        public void SetState(WorkspaceState state)
        {
            State = state;
            _owner.Broadcast(binding =>
            {
                if (binding.Workspaces.TryGetValue(this, out var handle))
                {
                    handle.SendState((ExtWorkspaceHandleV1.State)state);
                }
            });
        }

        public void SetName(string name)
        {
            Name = name;
            _owner.Broadcast(binding =>
            {
                if (binding.Workspaces.TryGetValue(this, out var handle))
                {
                    handle.SendName(name);
                }
            });
        }

        public void SetCoordinates(uint[]? coordinates)
        {
            Coordinates = coordinates;
            if (coordinates is null)
            {
                return;
            }

            _owner.Broadcast(binding =>
            {
                if (binding.Workspaces.TryGetValue(this, out var handle))
                {
                    handle.SendCoordinates(MemoryMarshal.AsBytes(coordinates.AsSpan()));
                }
            });
        }

        public void AssignTo(WorkspaceGroup group)
        {
            var previous = Group;
            Group = group;
            _owner.Broadcast(binding =>
            {
                if (!binding.Workspaces.TryGetValue(this, out var handle))
                {
                    return;
                }

                if (previous is not null && binding.Groups.TryGetValue(previous, out var old))
                {
                    old.SendWorkspaceLeave(handle);
                }

                if (binding.Groups.TryGetValue(group, out var next))
                {
                    next.SendWorkspaceEnter(handle);
                }
            });
        }

        public void Remove()
        {
            _owner._workspaces.Remove(this);
            var group = Group;
            Group = null;
            _owner.Broadcast(binding =>
            {
                if (!binding.Workspaces.TryGetValue(this, out var handle))
                {
                    return;
                }

                if (group is not null && binding.Groups.TryGetValue(group, out var owner))
                {
                    owner.SendWorkspaceLeave(handle);
                }

                handle.SendRemoved();
            });
        }

        internal void RaiseActivateRequested() => ActivateRequested?.Invoke();

        internal void RaiseDeactivateRequested() => DeactivateRequested?.Invoke();

        internal void RaiseRemoveRequested() => RemoveRequested?.Invoke();

        internal void RaiseAssignRequested(WorkspaceGroup group) => AssignRequested?.Invoke(group);
    }
}
