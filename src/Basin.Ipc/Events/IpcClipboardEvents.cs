using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcClipboardEvents : IpcCoalescedSource
{
    private readonly ISelectionStore _store;
    private readonly Action<SelectionKind> _changed;
    private bool _clipboard;
    private bool _primary;

    public IpcClipboardEvents(IpcServer server, ISelectionStore store)
        : base(server)
    {
        _store = store;
        _changed = OnChanged;
    }

    protected override void Attach() => _store.SelectionChanged += _changed;

    protected override void Detach()
    {
        _store.SelectionChanged -= _changed;
        base.Detach();
    }

    protected override void Reset()
    {
        _clipboard = false;
        _primary = false;
    }

    protected override void Flush()
    {
        if (_clipboard)
        {
            Write("clipboard");
        }

        if (_primary)
        {
            Write("primary");
        }

        Reset();
    }

    private void Write(string kind)
    {
        Bus.Emit(IpcEventNames.ClipboardChanged, new IpcClipboardChanged(kind), IpcJsonContext.Default.IpcClipboardChanged);
    }

    private void OnChanged(SelectionKind kind)
    {
        if (kind == SelectionKind.Primary)
        {
            _primary = true;
        }
        else
        {
            _clipboard = true;
        }

        Arm();
    }
}
