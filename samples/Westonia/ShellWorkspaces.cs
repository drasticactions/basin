using Basin;
using Basin.Capabilities;
using Basin.Effects;
using Basin.Scene;

namespace Westonia;

internal sealed class ShellWorkspaces : IWorkspaceModel, IDisposable
{
    private readonly WorkspaceObservers _observers = new();
    private readonly ShellLayers _layers;
    private readonly WestonShell _shell;
    private readonly WorkspaceSet<SceneTree> _trees = new();
    private int _active;
    private Spring _slide;
    private int _slideFrom = -1;
    private int _slideHeight;
    private bool _sliding;

    public ShellWorkspaces(ShellLayers layers, WestonShell shell, int count)
    {
        _layers = layers;
        _shell = shell;
        Count = Math.Max(1, count);
        _trees.IdOf = tree => (ulong)(_trees.IndexOf(tree) + 1);
        _trees.Describe = tree =>
        {
            var index = _trees.IndexOf(tree);
            return new WorkspaceInfo(
                (ulong)(index + 1),
                (index + 1).ToString(),
                $"westonia-workspace-{index + 1}",
                index == _active ? WorkspaceStateFlags.Active : WorkspaceStateFlags.None,
                [(uint)index]);
        };
        for (var i = 0; i < Count; i++)
        {
            _trees.Add(new SceneTree(layers.Workspaces) { Enabled = i == 0 });
        }
    }

    public int Count { get; }

    public int Active => _active;

    public bool IsSliding => _sliding;

    public double SlideProgress => _sliding ? Math.Clamp(_slide.Current, 0.0, 1.0) : 1.0;

    public Action? Changed { get; set; }

    public Func<int>? OutputHeight { get; set; }

    public SceneTree TreeOf(int index) => _trees[Math.Clamp(index, 0, Count - 1)];

    public SceneTree ActiveTree => _trees[_active];

    public void Activate(int index)
    {
        index = ((index % Count) + Count) % Count;
        if (index == _active)
        {
            return;
        }

        _slideFrom = _active;
        _active = index;
        _trees.Active = _trees[index];
        _slideHeight = Math.Max(1, OutputHeight?.Invoke() ?? 720);
        _trees[_slideFrom].Enabled = true;
        _trees[_active].Enabled = true;
        _slide = new Spring(200.0, 0.0, 1.0) { Clip = SpringClip.Clamp };
        _sliding = true;
        Apply(0.0);

        _observers.Changed();
        Changed?.Invoke();
    }

    public void Step(long nowNanos)
    {
        if (!_sliding)
        {
            return;
        }

        _slide.Update(nowNanos);
        Apply(Math.Clamp(_slide.Current, 0.0, 1.0));

        if (!_slide.IsDone)
        {
            return;
        }

        _sliding = false;
        _trees[_slideFrom].SetPosition(0, 0);
        _trees[_slideFrom].Enabled = false;
        _trees[_active].SetPosition(0, 0);
        _slideFrom = -1;
    }

    private void Apply(double progress)
    {
        var direction = _active > _slideFrom ? 1 : -1;
        var travel = (int)(progress * _slideHeight);
        _trees[_slideFrom].SetPosition(0, -direction * travel);
        _trees[_active].SetPosition(0, (direction * _slideHeight) - (direction * travel));
    }

    public void Carry(ShellWindow window, int index)
    {
        index = ((index % Count) + Count) % Count;
        window.Workspace = index;
        window.Tree.Reparent(_trees[index]);
        _observers.MembersChanged();
        Changed?.Invoke();
    }

    public void Adopt(ShellWindow window)
    {
        window.Workspace = _active;
        window.Tree.Reparent(_trees[_active]);
        _observers.MembersChanged();
    }

    public int EnumerateGroups(Span<WorkspaceGroupInfo> groups)
    {
        if (groups.Length == 0)
        {
            return 0;
        }

        groups[0] = new WorkspaceGroupInfo(1, ClientsCanCreateWorkspaces: false);
        return 1;
    }

    public int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces)
    {
        if (groupId != 1)
        {
            return 0;
        }

        return _trees.Fill(workspaces);
    }

    public int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs)
    {
        if (groupId != 1 || Outputs is not { } lookup)
        {
            return 0;
        }

        return lookup(outputs);
    }

    public Func<Span<IOutput>, int>? Outputs { get; set; }

    public int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members)
    {
        var index = (int)workspaceId - 1;
        if (index < 0 || index >= Count)
        {
            return 0;
        }

        var count = 0;
        foreach (var window in _shell.Windows)
        {
            if (window.Workspace != index || count >= members.Length)
            {
                continue;
            }

            members[count++] = new WorkspaceMember((ulong)window.Window.Xdg.GetHashCode(), window.Geometry);
        }

        return count;
    }

    public bool Request(ulong targetId, in WorkspaceRequest request)
    {
        var index = (int)targetId - 1;
        switch (request.Kind)
        {
            case WorkspaceRequestKind.Activate when index >= 0 && index < Count:
                Activate(index);
                return true;
            case WorkspaceRequestKind.Assign when index >= 0 && index < Count:
                foreach (var window in _shell.Windows)
                {
                    if ((ulong)window.Window.Xdg.GetHashCode() == request.ToplevelId)
                    {
                        Carry(window, index);
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    public void AddObserver(IWorkspaceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IWorkspaceObserver observer) => _observers.Remove(observer);

    public void Dispose()
    {
        foreach (var tree in _trees)
        {
            if (!tree.IsDestroyed)
            {
                tree.Destroy();
            }
        }
    }
}
