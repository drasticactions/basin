using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcPowerEvents : IpcCoalescedSource
{
    private readonly IOutputPower _power;
    private readonly Action<IOutput> _changed;
    private readonly List<IOutput> _pending = new(4);

    public IpcPowerEvents(IpcServer server, IOutputPower power)
        : base(server)
    {
        _power = power;
        _changed = OnChanged;
    }

    protected override void Attach() => _power.PowerChanged += _changed;

    protected override void Detach()
    {
        _power.PowerChanged -= _changed;
        base.Detach();
    }

    protected override void Reset() => _pending.Clear();

    protected override void Flush()
    {
        foreach (var output in _pending)
        {
            Bus.Emit(IpcEventNames.OutputPower, new IpcOutputPowerChanged(output.Name, _power.IsOn(output)), IpcJsonContext.Default.IpcOutputPowerChanged);
        }

        _pending.Clear();
    }

    private void OnChanged(IOutput output)
    {
        if (!_pending.Contains(output))
        {
            _pending.Add(output);
        }

        Arm();
    }
}
