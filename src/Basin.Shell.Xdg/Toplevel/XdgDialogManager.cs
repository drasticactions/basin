using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public sealed class XdgDialogManager : IDisposable
{
    public const int Version = 1;

    private const uint ErrorAlreadyUsed = 0;

    private readonly WlGlobal _global;
    private readonly HashSet<XdgToplevelWindow> _dialogs = [];

    public XdgDialogManager(WlServerDisplay display)
    {
        _global = display.CreateGlobal(XdgWmDialogV1.Interface, Version, OnBind);
    }

    public event Action<XdgToplevelWindow, bool>? ModalChanged;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new XdgWmDialogV1Resource(client, version, id);
        manager.GetXdgDialog += (_, e) =>
        {
            var dialog = new XdgDialogV1Resource(client, manager.Version, e.Id);
            if (e.Toplevel is null || XdgToplevelRegistry.Resolve(e.Toplevel) is not { } toplevel)
            {
                return;
            }

            if (!_dialogs.Add(toplevel))
            {
                manager.PostError(ErrorAlreadyUsed, "toplevel already has a dialog object");
                return;
            }

            dialog.SetModal += (_, _) => ModalChanged?.Invoke(toplevel, true);
            dialog.UnsetModal += (_, _) => ModalChanged?.Invoke(toplevel, false);
            dialog.Destroyed += (_, _) => _dialogs.Remove(toplevel);
        };
    }
}
