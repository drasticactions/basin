using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <c>xdg_wm_dialog_v1</c> global. It reports when a client makes a toplevel modal or not modal.
/// </summary>
public sealed class XdgDialogManager : IDisposable, IXdgWmDialogV1Handler
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

    private void OnBind(WlClient client, uint version, uint id) => _ = new XdgWmDialogV1Resource(client, version, id, this);

    void IXdgWmDialogV1Handler.Destroy(XdgWmDialogV1Resource resource)
    {
    }

    void IXdgWmDialogV1Handler.GetXdgDialog(XdgWmDialogV1Resource resource, in XdgWmDialogV1Resource.GetXdgDialogEventArgs args)
    {
        if (args.Toplevel is null || XdgToplevelRegistry.Resolve(args.Toplevel) is not { } toplevel)
        {
            _ = new XdgDialogV1Resource(resource.Client, resource.Version, args.Id);
            return;
        }

        if (!_dialogs.Add(toplevel))
        {
            _ = new XdgDialogV1Resource(resource.Client, resource.Version, args.Id);
            resource.PostError(ErrorAlreadyUsed, "The toplevel already has a dialog object.");
            return;
        }

        _ = new Dialog(this, resource.Client, resource.Version, args.Id, toplevel);
    }

    private sealed class Dialog : IXdgDialogV1Handler
    {
        private readonly XdgDialogManager _owner;
        private readonly XdgToplevelWindow _toplevel;
        private bool _inert;

        internal Dialog(XdgDialogManager owner, WlClient client, uint version, uint id, XdgToplevelWindow toplevel)
        {
            _owner = owner;
            _toplevel = toplevel;
            var dialog = new XdgDialogV1Resource(client, version, id, this);
            toplevel.Destroyed += OnToplevelDestroyed;
            dialog.Destroyed += OnDialogDestroyed;
        }

        void IXdgDialogV1Handler.Destroy(XdgDialogV1Resource resource)
        {
        }

        void IXdgDialogV1Handler.SetModal(XdgDialogV1Resource resource)
        {
            if (!_inert)
            {
                _owner.ModalChanged?.Invoke(_toplevel, true);
            }
        }

        void IXdgDialogV1Handler.UnsetModal(XdgDialogV1Resource resource)
        {
            if (!_inert)
            {
                _owner.ModalChanged?.Invoke(_toplevel, false);
            }
        }

        private void OnToplevelDestroyed()
        {
            _inert = true;
            _owner._dialogs.Remove(_toplevel);
        }

        private void OnDialogDestroyed(object? sender, EventArgs e)
        {
            if (!_inert)
            {
                _toplevel.Destroyed -= OnToplevelDestroyed;
                _owner._dialogs.Remove(_toplevel);
            }
        }
    }
}
