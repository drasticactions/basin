using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <c>xdg_wm_dialog_v1</c> global. It reports when a client makes a toplevel modal or not modal.
/// </summary>
public sealed class XdgDialogManager : IDisposable
{
    /// <summary>
    /// The version of <c>xdg_wm_dialog_v1</c> that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorAlreadyUsed = 0;

    private readonly WlGlobal _global;
    private readonly HashSet<XdgToplevelWindow> _dialogs = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgDialogManager"/> class. It creates the global on <paramref name="display"/> at once.
    /// </summary>
    /// <param name="display">The display that advertises the global.</param>
    public XdgDialogManager(WlServerDisplay display)
    {
        _global = display.CreateGlobal(XdgWmDialogV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a client sets or unsets the modal hint of a toplevel. The arguments are the toplevel and <see langword="true"/> for modal. It never occurs for a destroyed toplevel, because its dialog object is then inert.
    /// </summary>
    public event Action<XdgToplevelWindow, bool>? ModalChanged;

    /// <summary>
    /// Destroys the global.
    /// </summary>
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
                manager.PostError(ErrorAlreadyUsed, "The toplevel already has a dialog object.");
                return;
            }

            var inert = false;
            void OnToplevelDestroyed()
            {
                inert = true;
                _dialogs.Remove(toplevel);
            }

            toplevel.Destroyed += OnToplevelDestroyed;
            dialog.SetModal += (_, _) =>
            {
                if (!inert)
                {
                    ModalChanged?.Invoke(toplevel, true);
                }
            };
            dialog.UnsetModal += (_, _) =>
            {
                if (!inert)
                {
                    ModalChanged?.Invoke(toplevel, false);
                }
            };
            dialog.Destroyed += (_, _) =>
            {
                if (!inert)
                {
                    toplevel.Destroyed -= OnToplevelDestroyed;
                    _dialogs.Remove(toplevel);
                }
            };
        };
    }
}
