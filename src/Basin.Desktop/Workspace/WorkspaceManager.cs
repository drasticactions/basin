using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>ext_workspace_manager_v1</c> global. It shows groups and workspaces that the consumer creates, or projects them from an <see cref="IWorkspaceModel"/>.
/// </summary>
public sealed class WorkspaceManager : IWorkspaceObserver, IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal _global;
    private readonly List<Binding> _bindings = [];
    private readonly List<WorkspaceGroup> _groups = [];
    private readonly List<Workspace> _workspaces = [];

    internal sealed class Binding : IExtWorkspaceManagerV1Handler
    {
        public Binding(WlClient client, uint version, uint id)
        {
            Manager = new ExtWorkspaceManagerV1Resource(client, version, id, this);
        }

        public ExtWorkspaceManagerV1Resource Manager { get; }

        public Dictionary<WorkspaceGroup, ExtWorkspaceGroupHandleV1Resource> Groups { get; } = [];

        public Dictionary<Workspace, ExtWorkspaceHandleV1Resource> Workspaces { get; } = [];

        public List<Action> Pending { get; } = [];

        public bool Stopped { get; private set; }

        void IExtWorkspaceManagerV1Handler.Stop(ExtWorkspaceManagerV1Resource resource)
        {
            Stopped = true;
            Manager.SendFinished();
        }

        void IExtWorkspaceManagerV1Handler.Commit(ExtWorkspaceManagerV1Resource resource)
        {
            foreach (var request in Pending)
            {
                request();
            }

            Pending.Clear();
        }
    }

    private sealed class GroupRequests(Binding binding, WorkspaceGroup group) : IExtWorkspaceGroupHandleV1Handler
    {
        void IExtWorkspaceGroupHandleV1Handler.CreateWorkspace(ExtWorkspaceGroupHandleV1Resource resource, in ExtWorkspaceGroupHandleV1Resource.CreateWorkspaceEventArgs args)
        {
            var name = args.Workspace;
            if (!group.IsRemoved)
            {
                binding.Pending.Add(() => group.RaiseCreateRequested(name));
            }
        }

        void IExtWorkspaceGroupHandleV1Handler.Destroy(ExtWorkspaceGroupHandleV1Resource resource)
        {
        }
    }

    private sealed class WorkspaceRequests(Binding binding, Workspace workspace) : IExtWorkspaceHandleV1Handler
    {
        void IExtWorkspaceHandleV1Handler.Activate(ExtWorkspaceHandleV1Resource resource) =>
            binding.Pending.Add(workspace.RaiseActivateRequested);

        void IExtWorkspaceHandleV1Handler.Deactivate(ExtWorkspaceHandleV1Resource resource) =>
            binding.Pending.Add(workspace.RaiseDeactivateRequested);

        void IExtWorkspaceHandleV1Handler.Remove(ExtWorkspaceHandleV1Resource resource) =>
            binding.Pending.Add(workspace.RaiseRemoveRequested);

        void IExtWorkspaceHandleV1Handler.Assign(ExtWorkspaceHandleV1Resource resource, in ExtWorkspaceHandleV1Resource.AssignEventArgs args)
        {
            foreach (var (group, groupHandle) in binding.Groups)
            {
                if (groupHandle.RawHandle == args.WorkspaceGroupHandle)
                {
                    if (!group.IsRemoved)
                    {
                        binding.Pending.Add(() => workspace.RaiseAssignRequested(group));
                    }

                    break;
                }
            }
        }

        void IExtWorkspaceHandleV1Handler.Destroy(ExtWorkspaceHandleV1Resource resource)
        {
        }
    }

    private readonly IWorkspaceModel? _model;
    private readonly Dictionary<ulong, WorkspaceGroup> _modelGroups = [];
    private readonly Dictionary<WorkspaceGroup, ulong> _modelGroupIds = [];
    private readonly Dictionary<ulong, Workspace> _modelWorkspaces = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceManager"/> class and creates the global. With a model, it projects the model at once and follows it.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="model">The workspace model to project, or <see langword="null"/> when the consumer creates groups and workspaces itself.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
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

    /// <inheritdoc/>
    public void OnWorkspacesChanged() => Reproject();

    /// <summary>
    /// Does nothing. The protocol does not show the members of a workspace.
    /// </summary>
    public void OnWorkspaceMembersChanged()
    {
    }

    /// <summary>
    /// Stops observing the model and the outputs, and removes the global.
    /// </summary>
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

    /// <summary>
    /// Creates a group and announces it to every bound client.
    /// </summary>
    /// <param name="clientsCanCreateWorkspaces">Whether the group advertises the create-workspace capability.</param>
    /// <returns>The new group. It has no outputs until <see cref="WorkspaceGroup.AddOutput"/>.</returns>
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
        var binding = new Binding(client, version, id);
        var manager = binding.Manager;
        _bindings.Add(binding);
        manager.Destroyed += (_, _) => _bindings.Remove(binding);

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
        var handle = new ExtWorkspaceGroupHandleV1Resource(binding.Manager.Client, binding.Manager.Version, 0, new GroupRequests(binding, group));
        binding.Manager.SendWorkspaceGroup(handle);
        binding.Groups[group] = handle;
        handle.Destroyed += (_, _) => binding.Groups.Remove(group);

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
        var handle = new ExtWorkspaceHandleV1Resource(binding.Manager.Client, binding.Manager.Version, 0, new WorkspaceRequests(binding, workspace));
        binding.Manager.SendWorkspace(handle);
        binding.Workspaces[workspace] = handle;
        handle.Destroyed += (_, _) => binding.Workspaces.Remove(workspace);

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

    /// <summary>
    /// Represents a workspace group that clients see. Changes go to every bound client at once, followed by <c>done</c>.
    /// </summary>
    public sealed class WorkspaceGroup
    {
        private readonly WorkspaceManager _owner;

        internal WorkspaceGroup(WorkspaceManager owner, bool clientsCanCreateWorkspaces)
        {
            _owner = owner;
            ClientsCanCreateWorkspaces = clientsCanCreateWorkspaces;
        }

        /// <summary>
        /// Gets a value indicating whether clients can ask the group for a new workspace.
        /// </summary>
        public bool ClientsCanCreateWorkspaces { get; }

        /// <summary>
        /// Gets a value indicating whether <see cref="Remove"/> removed the group. A removed group takes no workspaces and raises no events.
        /// </summary>
        public bool IsRemoved { get; private set; }

        internal List<OutputGlobal> Outputs { get; } = [];

        /// <summary>
        /// Occurs when a client commits a request for a new workspace in the group. The argument is the requested name. The consumer decides whether to create it.
        /// </summary>
        public event Action<string>? CreateWorkspaceRequested;

        /// <summary>
        /// Adds an output to the group and sends <c>output_enter</c> to every bound client.
        /// </summary>
        /// <param name="output">The output to add.</param>
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

        /// <summary>
        /// Removes an output from the group and sends <c>output_leave</c> to every bound client.
        /// </summary>
        /// <param name="output">The output to remove.</param>
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

        /// <summary>
        /// Creates a workspace in the group and announces it to every bound client.
        /// </summary>
        /// <param name="name">The name that clients see.</param>
        /// <param name="id">A stable id that clients can keep across sessions, or <see langword="null"/>.</param>
        /// <param name="state">The initial state flags.</param>
        /// <param name="coordinates">The position in a grid of workspaces, or <see langword="null"/>.</param>
        /// <returns>The new workspace.</returns>
        /// <exception cref="InvalidOperationException">The group is removed.</exception>
        public Workspace CreateWorkspace(string name, string? id = null, WorkspaceState state = 0, uint[]? coordinates = null)
        {
            if (IsRemoved)
            {
                throw new InvalidOperationException("The workspace group is removed.");
            }

            var workspace = new Workspace(_owner, this, name, id, state, coordinates);
            _owner.OnWorkspaceAdded(workspace);
            return workspace;
        }

        /// <summary>
        /// Removes the group. Each workspace in it leaves it first and has no group after the call. Every bound client gets the <c>workspace_leave</c> events, then <c>removed</c>, then one <c>done</c>. A second call does nothing.
        /// </summary>
        public void Remove()
        {
            if (IsRemoved)
            {
                return;
            }

            IsRemoved = true;
            _owner._groups.Remove(this);
            var orphans = new List<Workspace>();
            foreach (var workspace in _owner._workspaces)
            {
                if (workspace.Group == this)
                {
                    workspace.Orphan();
                    orphans.Add(workspace);
                }
            }

            _owner.Broadcast(binding =>
            {
                if (!binding.Groups.Remove(this, out var handle))
                {
                    return;
                }

                foreach (var workspace in orphans)
                {
                    if (binding.Workspaces.TryGetValue(workspace, out var workspaceHandle))
                    {
                        handle.SendWorkspaceLeave(workspaceHandle);
                    }
                }

                handle.SendRemoved();
            });
            foreach (var output in Outputs)
            {
                _owner.UnwatchOutput(output);
            }
        }

        internal void RaiseCreateRequested(string name)
        {
            if (!IsRemoved)
            {
                CreateWorkspaceRequested?.Invoke(name);
            }
        }
    }

    /// <summary>
    /// Lists the state flags of a workspace, with the protocol's values. A workspace can have more than one flag at a time.
    /// </summary>
    [Flags]
    public enum WorkspaceState : uint
    {
        /// <summary>
        /// The workspace is shown.
        /// </summary>
        Active = 1,
        /// <summary>
        /// The workspace needs attention.
        /// </summary>
        Urgent = 2,
        /// <summary>
        /// Clients must not show the workspace in a workspace switcher.
        /// </summary>
        Hidden = 4,
    }

    /// <summary>
    /// Represents a workspace that clients see. Changes go to every bound client at once, followed by <c>done</c>.
    /// </summary>
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

        /// <summary>
        /// Gets the group that holds the workspace. It is <see langword="null"/> after <see cref="Remove"/>, and after <see cref="WorkspaceGroup.Remove"/> removes its group.
        /// </summary>
        public WorkspaceGroup? Group { get; private set; }

        /// <summary>
        /// Gets the name that clients see.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the stable id that clients can keep across sessions, or <see langword="null"/>.
        /// </summary>
        public string? Id { get; }

        /// <summary>
        /// Gets the state flags that clients see.
        /// </summary>
        public WorkspaceState State { get; private set; }

        /// <summary>
        /// Gets the position in a grid of workspaces, or <see langword="null"/>.
        /// </summary>
        public uint[]? Coordinates { get; private set; }

        /// <summary>
        /// Occurs when a client commits a request to activate the workspace. The consumer decides whether to act.
        /// </summary>
        public event Action? ActivateRequested;

        /// <summary>
        /// Occurs when a client commits a request to deactivate the workspace. The consumer decides whether to act.
        /// </summary>
        public event Action? DeactivateRequested;

        /// <summary>
        /// Occurs when a client commits a request to remove the workspace. The consumer decides whether to act.
        /// </summary>
        public event Action? RemoveRequested;

        /// <summary>
        /// Occurs when a client commits a request to move the workspace to another group. The argument is the target group.
        /// </summary>
        public event Action<WorkspaceGroup>? AssignRequested;

        /// <summary>
        /// Sets the state flags and sends them to every bound client.
        /// </summary>
        /// <param name="state">The new state flags.</param>
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

        /// <summary>
        /// Sets the name and sends it to every bound client.
        /// </summary>
        /// <param name="name">The new name.</param>
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

        /// <summary>
        /// Sets the grid position. Only a non-null value goes to the bound clients.
        /// </summary>
        /// <param name="coordinates">The new position, or <see langword="null"/> to clear it locally.</param>
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

        /// <summary>
        /// Moves the workspace to another group and sends <c>workspace_leave</c> and <c>workspace_enter</c> to every bound client.
        /// </summary>
        /// <param name="group">The new group.</param>
        /// <exception cref="ArgumentNullException"><paramref name="group"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="group"/> is removed.</exception>
        public void AssignTo(WorkspaceGroup group)
        {
            ArgumentNullException.ThrowIfNull(group);
            if (group.IsRemoved)
            {
                throw new InvalidOperationException("The workspace group is removed.");
            }

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

        /// <summary>
        /// Removes the workspace from its group and sends <c>removed</c> to every bound client.
        /// </summary>
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

        internal void Orphan() => Group = null;

        internal void RaiseActivateRequested() => ActivateRequested?.Invoke();

        internal void RaiseDeactivateRequested() => DeactivateRequested?.Invoke();

        internal void RaiseRemoveRequested() => RemoveRequested?.Invoke();

        internal void RaiseAssignRequested(WorkspaceGroup group)
        {
            if (!group.IsRemoved)
            {
                AssignRequested?.Invoke(group);
            }
        }
    }
}
