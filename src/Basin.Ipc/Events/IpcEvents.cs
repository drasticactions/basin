using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcEvents
{
    public static void Declare(IpcServer server, IpcDescribe describe)
    {
        var bus = server.Events;
        if (describe.Toplevels is { } model)
        {
            var toplevels = new IpcToplevelEvents(server, describe, model);
            bus.DeclareLibrary(IpcEventNames.WindowAdded, toplevels);
            bus.DeclareLibrary(IpcEventNames.WindowChanged, toplevels);
            bus.DeclareLibrary(IpcEventNames.WindowRemoved, toplevels);
            bus.DeclareLibrary(IpcEventNames.WindowFocused, toplevels);
        }

        if (describe.Stack is { } stack)
        {
            bus.DeclareLibrary(IpcEventNames.StackChanged, new IpcStackEvents(server, describe, stack));
        }

        if (describe.Outputs is not null || describe.Layout is not null)
        {
            bus.DeclareLibrary(IpcEventNames.OutputChanged, new IpcOutputEvents(server, describe));
        }

        if (describe.Power is { } power)
        {
            bus.DeclareLibrary(IpcEventNames.OutputPower, new IpcPowerEvents(server, power));
        }

        if (describe.Workspaces is { } workspaces)
        {
            bus.DeclareLibrary(IpcEventNames.WorkspaceChanged, new IpcWorkspaceEvents(server, describe, workspaces));
        }

        if (describe.Find<ILockState>() is { } locked)
        {
            IpcLockObserver? observer = null;
            bus.DeclareLibrary(IpcEventNames.LockChanged, new IpcFlagEvents(
                server,
                IpcEventNames.LockChanged,
                changed =>
                {
                    observer = new IpcLockObserver(changed);
                    locked.AddObserver(observer);
                },
                _ =>
                {
                    if (observer is not null)
                    {
                        locked.RemoveObserver(observer);
                        observer = null;
                    }
                },
                (bus, name) => bus.Emit(name, new IpcLockStatus(locked.IsLocked), IpcJsonContext.Default.IpcLockStatus)));
        }

        if (describe.Find<IIdleSource>() is { } idle)
        {
            bus.DeclareLibrary(IpcEventNames.IdleInhibitChanged, new IpcFlagEvents(
                server,
                IpcEventNames.IdleInhibitChanged,
                changed => idle.InhibitionChanged += changed,
                changed => idle.InhibitionChanged -= changed,
                (bus, name) => bus.Emit(name, new IpcIdleInhibited(idle.IsInhibited), IpcJsonContext.Default.IpcIdleInhibited)));
        }

        if (describe.Find<IActiveKeymap>() is { } keymap)
        {
            bus.DeclareLibrary(IpcEventNames.KeyboardKeymapChanged, new IpcFlagEvents(
                server,
                IpcEventNames.KeyboardKeymapChanged,
                changed => keymap.KeymapChanged += changed,
                changed => keymap.KeymapChanged -= changed,
                (bus, name) => bus.Emit(name, new IpcKeymapInfo(null, keymap.KeymapBuffer?.Size ?? 0), IpcJsonContext.Default.IpcKeymapInfo)));
        }

        if (describe.Find<ISelectionStore>() is { } selection)
        {
            bus.DeclareLibrary(IpcEventNames.ClipboardChanged, new IpcClipboardEvents(server, selection));
        }

        if (describe.Find<IGlobalShortcuts>() is { } shortcuts)
        {
            bus.DeclareLibrary(IpcEventNames.ShortcutActivated, new IpcShortcutEvents(server, shortcuts));
        }
    }
}
