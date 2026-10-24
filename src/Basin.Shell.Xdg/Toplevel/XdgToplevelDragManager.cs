using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <c>xdg_toplevel_drag_manager_v1</c> global. It reports which toplevel rides the drag in flight. Moving the window and removing it from the drop-target hit test are the consumer's work.
/// </summary>
public sealed class XdgToplevelDragManager : IDisposable, IXdgToplevelDragManagerV1Handler
{
    /// <summary>
    /// The version of <c>xdg_toplevel_drag_manager_v1</c> that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly IDragTracker? _drags;
    private readonly Dictionary<WlDataSourceResource, ToplevelDrag> _bySource = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgToplevelDragManager"/> class. It creates the global on <paramref name="display"/> at once.
    /// </summary>
    /// <param name="display">The display that advertises the global.</param>
    /// <param name="drags">The drag tracker of the seat. With <see langword="null"/>, no toplevel is ever attached.</param>
    public XdgToplevelDragManager(WlServerDisplay display, IDragTracker? drags)
    {
        ArgumentNullException.ThrowIfNull(display);
        _drags = drags;
        _global = display.CreateGlobal(XdgToplevelDragManagerV1.Interface, Version, OnBind);
        if (_drags is { } tracker)
        {
            tracker.DragChanged += Refresh;
        }
    }

    /// <summary>
    /// Gets the toplevel that rides the drag in flight, or <see langword="null"/> when no drag runs or no toplevel is attached to it.
    /// </summary>
    public ToplevelDragAttachment? Attachment { get; private set; }

    /// <summary>
    /// Occurs when <see cref="Attachment"/> changes. Unmapping or destroying the attached toplevel detaches it.
    /// </summary>
    public event Action<ToplevelDragAttachment?>? AttachmentChanged;

    /// <summary>
    /// Stops watching the drag tracker and destroys the global.
    /// </summary>
    public void Dispose()
    {
        if (_drags is { } tracker)
        {
            tracker.DragChanged -= Refresh;
        }

        _global.Dispose();
    }

    private void Refresh()
    {
        var source = _drags?.DraggingSource?.Resource;
        var attachment = source is not null && _bySource.TryGetValue(source, out var drag) ? drag.Attachment : null;
        if (attachment != Attachment)
        {
            Attachment = attachment;
            AttachmentChanged?.Invoke(attachment);
        }
    }

    private void OnBind(WlClient client, uint version, uint id) => _ = new XdgToplevelDragManagerV1Resource(client, version, id, this);

    void IXdgToplevelDragManagerV1Handler.Destroy(XdgToplevelDragManagerV1Resource resource)
    {
    }

    void IXdgToplevelDragManagerV1Handler.GetXdgToplevelDrag(XdgToplevelDragManagerV1Resource resource, in XdgToplevelDragManagerV1Resource.GetXdgToplevelDragEventArgs args)
    {
        if (args.DataSource is not { } source)
        {
            _ = new XdgToplevelDragV1Resource(resource.Client, resource.Version, args.Id);
            return;
        }

        if (_bySource.ContainsKey(source))
        {
            _ = new XdgToplevelDragV1Resource(resource.Client, resource.Version, args.Id);
            resource.PostError(
                (uint)XdgToplevelDragManagerV1.Error.InvalidSource,
                "The data source already has a toplevel drag.");
            return;
        }

        _bySource[source] = new ToplevelDrag(this, resource.Client, resource.Version, args.Id, source);
    }

    private sealed class ToplevelDrag : IXdgToplevelDragV1Handler
    {
        private readonly XdgToplevelDragManager _owner;
        private readonly XdgToplevelDragV1Resource _resource;
        private readonly WlDataSourceResource _source;
        private XdgToplevelWindow? _toplevel;
        private int _offsetX;
        private int _offsetY;

        internal ToplevelDrag(XdgToplevelDragManager owner, WlClient client, uint version, uint id, WlDataSourceResource source)
        {
            _owner = owner;
            var resource = new XdgToplevelDragV1Resource(client, version, id, this);
            _resource = resource;
            _source = source;

            resource.Destroyed += (_, _) =>
            {
                Detach();
                _owner._bySource.Remove(_source);
            };
            source.Destroyed += (_, _) => _owner._bySource.Remove(_source);
        }

        void IXdgToplevelDragV1Handler.Destroy(XdgToplevelDragV1Resource resource)
        {
            if (_owner._drags?.DraggingSource?.Resource == _source)
            {
                resource.PostError((uint)XdgToplevelDragV1.Error.OngoingDrag, "The drag has not ended.");
            }
        }

        void IXdgToplevelDragV1Handler.Attach(XdgToplevelDragV1Resource resource, in XdgToplevelDragV1Resource.AttachEventArgs args) => OnAttach(in args);

        internal ToplevelDragAttachment? Attachment =>
            _toplevel is { } toplevel ? new ToplevelDragAttachment(toplevel, _offsetX, _offsetY) : null;

        private void OnAttach(in XdgToplevelDragV1Resource.AttachEventArgs e)
        {
            if (_toplevel is not null)
            {
                _resource.PostError((uint)XdgToplevelDragV1.Error.ToplevelAttached, "A toplevel is already attached to the drag.");
                return;
            }

            if (XdgToplevels.Resolve(e.Toplevel) is not { } toplevel)
            {
                return;
            }

            _toplevel = toplevel;
            (_offsetX, _offsetY) = (e.XOffset, e.YOffset);
            toplevel.Xdg.Unmapped += Detach;
            toplevel.Destroyed += Detach;
            _owner.Refresh();
        }

        private void Detach()
        {
            if (_toplevel is { } toplevel)
            {
                toplevel.Xdg.Unmapped -= Detach;
                toplevel.Destroyed -= Detach;
                _toplevel = null;
                _owner.Refresh();
            }
        }
    }
}
