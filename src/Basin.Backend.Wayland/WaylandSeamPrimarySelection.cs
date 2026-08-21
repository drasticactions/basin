using Basin.Backend.Wayland.Protocol;
using Basin.Capabilities;
using Wayland;

namespace Basin.Backend.Wayland;

internal sealed class WaylandSeamPrimarySelection : WaylandSeamSelectionBridge
{
    private readonly ZwpPrimarySelectionDeviceV1 _device;
    private ZwpPrimarySelectionOfferV1? _pending;
    private List<string>? _pendingMimeTypes;
    private ZwpPrimarySelectionOfferV1? _current;
    private ZwpPrimarySelectionSourceV1? _source;

    internal WaylandSeamPrimarySelection(WaylandBackend backend, ISelectionStore store, ZwpPrimarySelectionDeviceV1 device)
        : base(backend, store, SelectionKind.Primary)
    {
        _device = device;
        _device.DataOffer += OnParentDataOffer;
        _device.Selection += OnParentSelection;
    }

    public override void Dispose()
    {
        _device.DataOffer -= OnParentDataOffer;
        _device.Selection -= OnParentSelection;
        DropParentSource();
        DiscardPending();
        WaylandBackend.DisposeParent(_current);
        _current = null;
        base.Dispose();
    }

    protected override void SendFromParent(string mimeType, ClientFd fd)
    {
        if (IsDisposed || _current is not { IsDestroyed: false } offer)
        {
            fd.Close();
            return;
        }

        offer.Receive(mimeType, fd.Value);
        Backend.Flush();
        fd.Close();
    }

    protected override void PushToParent(ReadOnlySpan<string> mimeTypes, uint serial)
    {
        if (IsDisposed || Backend.ParentPrimarySelectionManager is not { } manager)
        {
            return;
        }

        DropParentSource();
        var source = manager.CreateSource();
        _source = source;
        foreach (var mimeType in mimeTypes)
        {
            source.Offer(mimeType);
        }

        source.Send += OnParentSourceSend;
        source.Cancelled += OnParentSourceCancelled;
        _device.SetSelection(source, serial);
        Backend.Flush();
    }

    protected override void DropParentSource()
    {
        if (_source is { } source)
        {
            source.Send -= OnParentSourceSend;
            source.Cancelled -= OnParentSourceCancelled;
            WaylandBackend.DisposeParent(source);
            Backend.Flush();
        }

        _source = null;
    }

    private void OnParentSourceSend(object? sender, ZwpPrimarySelectionSourceV1.SendEventArgs e) =>
        ReceiveForParent(e.MimeType, e.Fd);

    private void OnParentSourceCancelled(object? sender, ZwpPrimarySelectionSourceV1.CancelledEventArgs e) =>
        DropParentSource();

    private void OnParentDataOffer(object? sender, ZwpPrimarySelectionDeviceV1.DataOfferEventArgs e)
    {
        DiscardPending();
        var mimeTypes = new List<string>();
        _pending = e.Offer;
        _pendingMimeTypes = mimeTypes;
        e.Offer.Offer += (_, offered) => mimeTypes.Add(offered.MimeType);
    }

    private void OnParentSelection(object? sender, ZwpPrimarySelectionDeviceV1.SelectionEventArgs e)
    {
        if (_source is { IsDestroyed: false })
        {
            DiscardPending();
            WaylandBackend.DisposeParent(_current);
            _current = null;
            return;
        }

        var replaced = _current;
        _current = null;
        if (e.Id is null || !ReferenceEquals(e.Id, _pending) || _pendingMimeTypes is not { Count: > 0 } mimeTypes)
        {
            DiscardPending();
            RetractFromGuests();
            WaylandBackend.DisposeParent(replaced);
            return;
        }

        _current = e.Id;
        _pending = null;
        _pendingMimeTypes = null;
        WaylandBackend.DisposeParent(replaced);
        OfferToGuests(mimeTypes);
    }

    private void DiscardPending()
    {
        WaylandBackend.DisposeParent(_pending);
        _pending = null;
        _pendingMimeTypes = null;
    }
}
