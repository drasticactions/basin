using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcToplevelEvents : IpcCoalescedSource, IToplevelObserver
{
    private readonly IToplevelModel _model;
    private readonly IpcDescribe _describe;
    private readonly List<ulong> _added = new(16);
    private readonly HashSet<ulong> _changed = new(64);
    private readonly List<ulong> _removed = new(16);
    private readonly Dictionary<ulong, ulong> _workspaceOf = new(16);
    private ulong _focused;

    public IpcToplevelEvents(IpcServer server, IpcDescribe describe, IToplevelModel model)
        : base(server)
    {
        _model = model;
        _describe = describe;
    }

    public void OnToplevelAdded(ulong toplevelId)
    {
        _added.Add(toplevelId);
        Arm();
    }

    public void OnToplevelChanged(ulong toplevelId)
    {
        _ = _changed.Add(toplevelId);
        Arm();
    }

    public void OnToplevelRemoved(ulong toplevelId)
    {
        _removed.Add(toplevelId);
        Arm();
    }

    protected override void Attach()
    {
        _focused = 0;
        foreach (var info in _describe.Windows())
        {
            if ((info.State & ToplevelState.Activated) != 0)
            {
                _focused = info.Id;
            }
        }

        _model.AddObserver(this);
    }

    protected override void Detach()
    {
        _model.RemoveObserver(this);
        base.Detach();
    }

    protected override void Reset()
    {
        _added.Clear();
        _changed.Clear();
        _removed.Clear();
    }

    protected override void Flush()
    {
        var workspaceOf = Bus.HasSubscribers(IpcEventNames.WindowAdded) || Bus.HasSubscribers(IpcEventNames.WindowChanged)
            ? _describe.WorkspaceOf(_workspaceOf)
            : null;
        foreach (var id in _added)
        {
            _ = _changed.Remove(id);
            if (_removed.Contains(id) || !_model.TryGet(id, out var info))
            {
                continue;
            }

            Describe(IpcEventNames.WindowAdded, info, workspaceOf);
            Focus(info);
        }

        foreach (var id in _changed)
        {
            if (_removed.Contains(id) || !_model.TryGet(id, out var info))
            {
                continue;
            }

            Describe(IpcEventNames.WindowChanged, info, workspaceOf);
            Focus(info);
        }

        foreach (var id in _removed)
        {
            if (_focused == id)
            {
                _focused = 0;
            }

            if (Bus.HasSubscribers(IpcEventNames.WindowRemoved))
            {
                Bus.Emit(IpcEventNames.WindowRemoved, new IpcWindowId(id), IpcJsonContext.Default.IpcWindowId);
            }
        }

        Reset();
    }

    private void Describe(string name, in ToplevelInfo info, Dictionary<ulong, ulong>? workspaceOf)
    {
        if (!Bus.HasSubscribers(name))
        {
            return;
        }

        Bus.Emit(name, _describe.Window(info, workspaceOf), IpcJsonContext.Default.IpcWindow);
    }

    private void Focus(in ToplevelInfo info)
    {
        if ((info.State & ToplevelState.Activated) == 0 || _focused == info.Id)
        {
            return;
        }

        _focused = info.Id;
        if (!Bus.HasSubscribers(IpcEventNames.WindowFocused))
        {
            return;
        }

        Bus.Emit(IpcEventNames.WindowFocused, new IpcWindowId(info.Id), IpcJsonContext.Default.IpcWindowId);
    }
}
