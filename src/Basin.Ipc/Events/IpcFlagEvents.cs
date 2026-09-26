namespace Basin.Ipc;

internal sealed class IpcFlagEvents : IpcCoalescedSource
{
    private readonly string _name;
    private readonly Action<Action> _subscribe;
    private readonly Action<Action> _unsubscribe;
    private readonly Action<IpcEventBus, string> _emit;
    private readonly Action _changed;
    private bool _dirty;

    public IpcFlagEvents(
        IpcServer server,
        string name,
        Action<Action> subscribe,
        Action<Action> unsubscribe,
        Action<IpcEventBus, string> emit)
        : base(server)
    {
        _name = name;
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
        _emit = emit;
        _changed = () =>
        {
            _dirty = true;
            Arm();
        };
    }

    public Action Changed => _changed;

    protected override void Attach() => _subscribe(_changed);

    protected override void Detach()
    {
        _unsubscribe(_changed);
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
        _emit(Bus, _name);
    }
}
