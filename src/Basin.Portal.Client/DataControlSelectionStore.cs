using Basin;
using Basin.Capabilities;
using Basin.Portal.Client.Protocol;
using Wayland;

namespace Basin.Portal.Client;

/// <summary>
/// Implements <see cref="ISelectionStore"/> over <c>ext_data_control_v1</c>, so that a portal client can read and set the clipboard of the compositor.
/// It supports only <see cref="SelectionKind.Clipboard"/>.
/// </summary>
public sealed class DataControlSelectionStore : ISelectionStore, IDisposable
{
    private readonly ExtDataControlManagerV1 _manager;
    private readonly List<string> _offerTypes = [];
    private ExtDataControlDeviceV1? _device;
    private DataSource? _ownSource;
    private ExtDataControlSourceV1? _wireSource;
    private ExtDataControlOfferV1? _offer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataControlSelectionStore"/> class and gets the data control device of the seat.
    /// </summary>
    /// <param name="manager">The bound data control manager.</param>
    /// <param name="seat">The seat whose selection the store follows.</param>
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

    /// <summary>
    /// Occurs when the compositor reports a new clipboard selection, including a selection that this store set. The argument is always <see cref="SelectionKind.Clipboard"/>.
    /// </summary>
    public event Action<SelectionKind>? SelectionChanged;

    /// <summary>
    /// Copies the MIME types of the last offer that the compositor sent.
    /// </summary>
    /// <param name="kind">The selection to read. Only the clipboard has types.</param>
    /// <param name="types">The span that receives the MIME types.</param>
    /// <returns>The number of types, 0 for a kind other than the clipboard, or -1 when the span is too short.</returns>
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

    /// <summary>
    /// Gets the source that this store set last for the clipboard. A selection that another client owns is not visible here.
    /// </summary>
    /// <param name="kind">The selection to read.</param>
    /// <returns>The source, or <see langword="null"/> when this store set none or the kind is not the clipboard.</returns>
    public DataSource? Current(SelectionKind kind) => kind == SelectionKind.Clipboard ? _ownSource : null;

    /// <summary>
    /// Offers a source as the clipboard of the compositor, or clears the clipboard. The compositor sends the change back as <see cref="SelectionChanged"/>.
    /// </summary>
    /// <param name="kind">The selection to set. Only the clipboard is accepted.</param>
    /// <param name="source">The source to offer, or <see langword="null"/> to clear the selection.</param>
    /// <param name="serial">The input serial. Data control needs no serial, so it is ignored.</param>
    /// <returns><see langword="false"/> for a kind other than the clipboard or after <see cref="Dispose"/>.</returns>
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

    /// <summary>
    /// Asks the owner of the current clipboard offer to write its data in one MIME type into an fd. The call always closes <paramref name="fd"/>.
    /// </summary>
    /// <param name="kind">The selection to read. Only the clipboard is accepted.</param>
    /// <param name="mimeType">The MIME type to receive.</param>
    /// <param name="fd">The write end of a pipe that receives the data.</param>
    /// <returns><see langword="false"/> when the kind is not the clipboard or no offer is present.</returns>
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

    /// <summary>
    /// Destroys the wire source and the data control device. Later calls to <see cref="SetSelection"/> return <see langword="false"/>.
    /// </summary>
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
