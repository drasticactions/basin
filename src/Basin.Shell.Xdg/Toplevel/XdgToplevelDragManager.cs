using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public sealed class XdgToplevelDragManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly IDragTracker? _drags;
    private readonly Dictionary<WlDataSourceResource, ToplevelDrag> _bySource = [];

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

    public ToplevelDragAttachment? Attachment { get; private set; }

    public event Action<ToplevelDragAttachment?>? AttachmentChanged;

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

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new XdgToplevelDragManagerV1Resource(client, version, id);
        manager.GetXdgToplevelDrag += (_, e) =>
        {
            var resource = new XdgToplevelDragV1Resource(client, manager.Version, e.Id);
            if (e.DataSource is not { } source)
            {
                return;
            }

            if (_bySource.ContainsKey(source))
            {
                manager.PostError(
                    (uint)XdgToplevelDragManagerV1.Error.InvalidSource,
                    "the data source already has a toplevel drag");
                return;
            }

            _bySource[source] = new ToplevelDrag(this, resource, source);
        };
    }

    private sealed class ToplevelDrag
    {
        private readonly XdgToplevelDragManager _owner;
        private readonly XdgToplevelDragV1Resource _resource;
        private readonly WlDataSourceResource _source;
        private XdgToplevelWindow? _toplevel;
        private int _offsetX;
        private int _offsetY;

        internal ToplevelDrag(XdgToplevelDragManager owner, XdgToplevelDragV1Resource resource, WlDataSourceResource source)
        {
            _owner = owner;
            _resource = resource;
            _source = source;

            resource.Attach += (_, e) => OnAttach(e);
            resource.DestroyRequest += (_, _) =>
            {
                if (_owner._drags?.DraggingSource?.Resource == _source)
                {
                    resource.PostError((uint)XdgToplevelDragV1.Error.OngoingDrag, "the drag has not ended");
                }
            };
            resource.Destroyed += (_, _) =>
            {
                Detach();
                _owner._bySource.Remove(_source);
            };
            source.Destroyed += (_, _) => _owner._bySource.Remove(_source);
        }

        internal ToplevelDragAttachment? Attachment =>
            _toplevel is { } toplevel ? new ToplevelDragAttachment(toplevel, _offsetX, _offsetY) : null;

        private void OnAttach(XdgToplevelDragV1Resource.AttachEventArgs e)
        {
            if (_toplevel is not null)
            {
                _resource.PostError((uint)XdgToplevelDragV1.Error.ToplevelAttached, "a toplevel is already attached");
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
