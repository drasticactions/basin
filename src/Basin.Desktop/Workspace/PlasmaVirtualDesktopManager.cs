using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class PlasmaVirtualDesktopManager : IWorkspaceObserver, IDisposable
{
    public const int Version = 4;

    private readonly WlGlobal _global;
    private readonly IWorkspaceModel? _model;
    private readonly List<Binding> _bindings = [];
    private List<DesktopSnapshot> _desktops = [];

    private sealed record DesktopSnapshot(
        ulong WorkspaceId, string Id, string Name, uint Position, bool Active, string? OutputName);

    private sealed class Binding
    {
        public required OrgKdePlasmaVirtualDesktopManagementResource Manager;
        public required Dictionary<string, List<OrgKdePlasmaVirtualDesktopResource>> Desktops;
    }

    public PlasmaVirtualDesktopManager(WlServerDisplay display, IWorkspaceModel? model)
    {
        ArgumentNullException.ThrowIfNull(display);
        _model = model;
        _global = display.CreateGlobal(OrgKdePlasmaVirtualDesktopManagement.Interface, Version, OnBind);
        if (_model is { } live)
        {
            live.AddObserver(this);
            _desktops = Compute();
        }
    }

    public void OnWorkspacesChanged() => Rebuild();

    public void OnWorkspaceMembersChanged()
    {
    }

    public void Dispose()
    {
        if (_model is { } live)
        {
            live.RemoveObserver(this);
        }

        _global.Dispose();
    }

    private List<DesktopSnapshot> Compute()
    {
        var result = new List<DesktopSnapshot>();
        if (_model is not { } model)
        {
            return result;
        }

        var groups = new WorkspaceGroupInfo[8];
        var groupCount = model.EnumerateGroups(groups);
        while (groupCount < 0)
        {
            groups = new WorkspaceGroupInfo[groups.Length * 2];
            groupCount = model.EnumerateGroups(groups);
        }

        var workspaces = new WorkspaceInfo[16];
        var outputs = new IOutput[4];
        var position = 0u;
        for (var i = 0; i < groupCount; i++)
        {
            var outputCount = model.EnumerateGroupOutputs(groups[i].Id, outputs);
            while (outputCount < 0)
            {
                outputs = new IOutput[outputs.Length * 2];
                outputCount = model.EnumerateGroupOutputs(groups[i].Id, outputs);
            }

            var outputName = outputCount > 0 ? outputs[0].Name : null;
            var count = model.EnumerateWorkspaces(groups[i].Id, workspaces);
            while (count < 0)
            {
                workspaces = new WorkspaceInfo[workspaces.Length * 2];
                count = model.EnumerateWorkspaces(groups[i].Id, workspaces);
            }

            for (var w = 0; w < count; w++)
            {
                var entry = workspaces[w];
                result.Add(new DesktopSnapshot(
                    entry.Id,
                    entry.Handle ?? $"ws-{entry.Id}",
                    entry.Name,
                    position++,
                    (entry.State & WorkspaceStateFlags.Active) != 0,
                    outputName));
            }
        }

        return result;
    }

    private DesktopSnapshot? Find(string desktopId) => _desktops.Find(d => d.Id == desktopId);

    private void Rebuild()
    {
        var previous = _desktops;
        _desktops = Compute();
        for (var i = _bindings.Count - 1; i >= 0; i--)
        {
            var binding = _bindings[i];
            if (!binding.Manager.IsDestroyed)
            {
                SyncBinding(binding, previous, _desktops);
            }
        }
    }

    private void SyncBinding(Binding binding, List<DesktopSnapshot> previous, List<DesktopSnapshot> current)
    {
        foreach (var old in previous)
        {
            if (current.Find(d => d.Id == old.Id) is null)
            {
                binding.Manager.SendDesktopRemoved(old.Id);
                if (binding.Desktops.Remove(old.Id, out var resources))
                {
                    foreach (var resource in resources)
                    {
                        if (!resource.IsDestroyed)
                        {
                            resource.SendRemoved();
                        }
                    }
                }
            }
        }

        foreach (var desktop in current)
        {
            var old = previous.Find(d => d.Id == desktop.Id);
            if (old is null)
            {
                binding.Manager.SendDesktopCreated(desktop.Id, desktop.Position);
                continue;
            }

            if (!binding.Desktops.TryGetValue(desktop.Id, out var resources))
            {
                continue;
            }

            foreach (var resource in resources)
            {
                if (resource.IsDestroyed)
                {
                    continue;
                }

                var dirty = false;
                if (old.Name != desktop.Name)
                {
                    resource.SendName(desktop.Name);
                    dirty = true;
                }

                if (old.Position != desktop.Position && resource.Version >= 3)
                {
                    resource.SendPosition(desktop.Position);
                    dirty = true;
                }

                if (old.Active != desktop.Active)
                {
                    if (desktop.Active)
                    {
                        resource.SendActivated();
                        if (resource.Version >= 4 && desktop.OutputName is { } outputName)
                        {
                            resource.SendOutputEntered(outputName);
                        }
                    }
                    else
                    {
                        resource.SendDeactivated();
                    }

                    dirty = true;
                }

                if (dirty)
                {
                    resource.SendDone();
                }
            }
        }

        binding.Manager.SendDone();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdePlasmaVirtualDesktopManagementResource(client, version, id);
        var binding = new Binding { Manager = manager, Desktops = [] };
        _bindings.Add(binding);
        manager.Destroyed += (_, _) => _bindings.Remove(binding);

        manager.GetVirtualDesktop += (_, e) =>
        {
            var resource = new OrgKdePlasmaVirtualDesktopResource(client, manager.Version, e.Id);
            WireDesktop(binding, resource, e.DesktopId);
        };
        manager.RequestCreateVirtualDesktop += (_, e) =>
        {
            if (_model is { } model)
            {
                var groups = new WorkspaceGroupInfo[8];
                var count = model.EnumerateGroups(groups);
                while (count < 0)
                {
                    groups = new WorkspaceGroupInfo[groups.Length * 2];
                    count = model.EnumerateGroups(groups);
                }

                if (count > 0)
                {
                    model.Request(groups[0].Id, new WorkspaceRequest(WorkspaceRequestKind.Create, e.Name));
                }
            }
        };
        manager.RequestRemoveVirtualDesktop += (_, e) =>
        {
            if (_model is { } model && Find(e.DesktopId) is { } desktop)
            {
                model.Request(desktop.WorkspaceId, new WorkspaceRequest(WorkspaceRequestKind.Remove));
            }
        };

        foreach (var desktop in _desktops)
        {
            manager.SendDesktopCreated(desktop.Id, desktop.Position);
        }

        if (version >= 2)
        {
            manager.SendRows(1);
        }

        manager.SendDone();
    }

    private void WireDesktop(Binding binding, OrgKdePlasmaVirtualDesktopResource resource, string desktopId)
    {
        resource.RequestActivate += (_, _) => Activate(desktopId);
        resource.RequestEnterOutput += (_, _) => Activate(desktopId);

        if (Find(desktopId) is not { } known)
        {
            resource.SendRemoved();
            return;
        }

        if (!binding.Desktops.TryGetValue(desktopId, out var resources))
        {
            binding.Desktops[desktopId] = resources = [];
        }

        resources.Add(resource);
        resource.Destroyed += (_, _) => resources.Remove(resource);

        resource.SendDesktopId(known.Id);
        resource.SendName(known.Name);
        if (resource.Version >= 3)
        {
            resource.SendPosition(known.Position);
        }

        if (known.Active)
        {
            resource.SendActivated();
            if (resource.Version >= 4 && known.OutputName is { } outputName)
            {
                resource.SendOutputEntered(outputName);
            }
        }

        resource.SendDone();
    }

    private void Activate(string desktopId)
    {
        if (_model is { } model && Find(desktopId) is { } desktop)
        {
            model.Request(desktop.WorkspaceId, new WorkspaceRequest(WorkspaceRequestKind.Activate));
        }
    }
}
