using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcShortcutEvents(IpcServer server, IGlobalShortcuts shortcuts)
    : IpcCoalescedSource(server), IGlobalShortcutObserver
{
    private readonly List<(string AppId, string Id, bool Pressed)> _pending = new(8);

    public void ShortcutRegistered(in GlobalShortcutInfo shortcut)
    {
    }

    public void ShortcutRemoved(in GlobalShortcutInfo shortcut)
    {
    }

    public void ShortcutActivated(in GlobalShortcutInfo shortcut, ulong timestampMs) => Queue(shortcut, true);

    public void ShortcutDeactivated(in GlobalShortcutInfo shortcut, ulong timestampMs) => Queue(shortcut, false);

    protected override void Attach() => shortcuts.AddObserver(this);

    protected override void Detach()
    {
        shortcuts.RemoveObserver(this);
        base.Detach();
    }

    protected override void Reset() => _pending.Clear();

    protected override void Flush()
    {
        foreach (var (appId, id, pressed) in _pending)
        {
            Bus.Emit(IpcEventNames.ShortcutActivated, new IpcShortcutActivated(appId, id, pressed), IpcJsonContext.Default.IpcShortcutActivated);
        }

        _pending.Clear();
    }

    private void Queue(in GlobalShortcutInfo shortcut, bool pressed)
    {
        _pending.Add((shortcut.AppId, shortcut.Id, pressed));
        Arm();
    }
}
