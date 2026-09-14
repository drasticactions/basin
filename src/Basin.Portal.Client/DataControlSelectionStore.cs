using Basin;
using Basin.Capabilities;
using Basin.Portal.Client.Protocol;
using Wayland;

namespace Basin.Portal.Client;

public sealed class DataControlSelectionStore : ISelectionStore, IDisposable
{
    private readonly ExtDataControlManagerV1 _manager;
    private readonly List<string> _offerTypes = [];
    private ExtDataControlDeviceV1? _device;
    private DataSource? _ownSource;
    private ExtDataControlSourceV1? _wireSource;
    private ExtDataControlOfferV1? _offer;

    public DataControlSelectionStore(ExtDataControlManagerV1 manager, WlSeat seat)
    {
        _manager = manager;
        var device = manager.GetDataDevice(seat);
        _device = device;
        device.DataOffer += (_, e) =>
        {
            _offerTypes.Clear();
            e.Id.Offer += (_, oe) => _offerTypes.Add(oe.MimeType);
            _offer = e.Id;
        };
        device.Selection += (_, e) =>
        {
            SelectionChanged?.Invoke(SelectionKind.Clipboard);
        };
    }

    public event Action<SelectionKind>? SelectionChanged;

    public int GetOffer(SelectionKind kind, Span<string> types)
    {
        if (kind != SelectionKind.Clipboard)
        {
            return 0;
        }

        if (types.Length < _offerTypes.Count)
        {
            return -1;
        }

        for (var i = 0; i < _offerTypes.Count; i++)
        {
            types[i] = _offerTypes[i];
        }

        return _offerTypes.Count;
    }

    public DataSource? Current(SelectionKind kind) => kind == SelectionKind.Clipboard ? _ownSource : null;

    public bool SetSelection(SelectionKind kind, DataSource? source, uint serial)
    {
        if (kind != SelectionKind.Clipboard || _device is not { } device)
        {
            return false;
        }

        if (_wireSource is { IsDestroyed: false } old)
        {
            old.Destroy();
        }

        _wireSource = null;
        _ownSource = source;
        if (source is null)
        {
            device.SetSelection(null);
            return true;
        }

        var wire = _manager.CreateDataSource();
        _wireSource = wire;
        foreach (var mime in source.MimeTypes)
        {
            wire.Offer(mime);
        }

        wire.Send += (_, e) => source.Send(e.MimeType, new ClientFd(e.Fd, null));
        wire.Cancelled += (_, _) =>
        {
            source.MarkDestroyed();
            if (!wire.IsDestroyed)
            {
                wire.Destroy();
            }
        };
        device.SetSelection(wire);
        return true;
    }

    public bool Receive(SelectionKind kind, string mimeType, ClientFd fd)
    {
        if (kind != SelectionKind.Clipboard || _offer is not { IsDestroyed: false } offer)
        {
            fd.Close();
            return false;
        }

        offer.Receive(mimeType, fd.Value);
        fd.Close();
        return true;
    }

    public void Dispose()
    {
        if (_wireSource is { IsDestroyed: false } wire)
        {
            wire.Destroy();
        }

        if (_device is { IsDestroyed: false } device)
        {
            device.Destroy();
        }

        _device = null;
    }
}
