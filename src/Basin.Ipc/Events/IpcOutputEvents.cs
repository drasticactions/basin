using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcOutputEvents : IpcCoalescedSource
{
    private readonly IpcDescribe _describe;
    private readonly Action _changed;
    private readonly Action<IReadOnlyList<OutputConfigurationEntry>> _applied;
    private bool _dirty;

    public IpcOutputEvents(IpcServer server, IpcDescribe describe)
        : base(server)
    {
        _describe = describe;
        _changed = OnChanged;
        _applied = _ => OnChanged();
    }

    protected override void Attach()
    {
        if (_describe.Outputs is { } set)
        {
            set.Changed += _changed;
        }

        if (_describe.Layout is { } layout)
        {
            layout.Changed += _changed;
        }

        if (_describe.Configuration is { } configuration)
        {
            configuration.Applied += _applied;
        }
    }

    protected override void Detach()
    {
        if (_describe.Outputs is { } set)
        {
            set.Changed -= _changed;
        }

        if (_describe.Layout is { } layout)
        {
            layout.Changed -= _changed;
        }

        if (_describe.Configuration is { } configuration)
        {
            configuration.Applied -= _applied;
        }

        base.Detach();
    }

    protected override void Reset() => _dirty = false;

    protected override void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        Bus.Emit(IpcEventNames.OutputChanged, _describe.OutputList(), IpcJsonContext.Default.IpcOutputList);
    }

    private void OnChanged()
    {
        _dirty = true;
        Arm();
    }
}
