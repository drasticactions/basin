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

        if (describe.Toplevels is not null && describe.Find<IFrameStats>() is { } frameStats)
        {
            bus.DeclareLibrary(IpcEventNames.FrameStats, new IpcFrameStatsEvents(server, describe, frameStats));
        }

        if (describe.Find<IInputDeviceConfiguration>() is { } devices)
        {
            var source = new IpcDeviceEvents(server, devices);
            bus.DeclareLibrary(IpcEventNames.DeviceAdded, source);
            bus.DeclareLibrary(IpcEventNames.DeviceRemoved, source);
            bus.DeclareLibrary(IpcEventNames.DeviceChanged, source);
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

        if (describe.Diagnostics is { } diagnostics)
        {
            bus.DeclareLibrary(IpcEventNames.DiagDamage, new IpcDiagDamageEvents(server, diagnostics));
            bus.DeclareLibrary(IpcEventNames.DiagFrame, new IpcDiagFrameEvents(server, describe, diagnostics));
            bus.AddLineFormatter(IpcEventNames.DiagDamage, IpcDiagLines.DamageEvent);
            bus.AddLineFormatter(IpcEventNames.DiagFrame, IpcDiagLines.FrameEvent);
            bus.DeclareLibrary(IpcEventNames.DiagPlane, new IpcDiagPlaneEvents(server, diagnostics));
            bus.AddLineFormatter(IpcEventNames.DiagPlane, IpcDiagLines.PlaneEvent);
        }

        if (describe.Find<ISurfaceDiagnostics>() is { } surfaces)
        {
            bus.DeclareLibrary(IpcEventNames.DiagCommit, new IpcDiagCommitEvents(server, describe, surfaces));
            bus.AddLineFormatter(IpcEventNames.DiagCommit, IpcDiagLines.CommitEvent);
        }

        if (describe.Find<IInputDiagnostics>() is { } input)
        {
            bus.DeclareLibrary(IpcEventNames.DiagInput, new IpcDiagInputEvents(server, describe, input));
            bus.AddLineFormatter(IpcEventNames.DiagInput, IpcDiagLines.InputEvent);
        }
    }
}
