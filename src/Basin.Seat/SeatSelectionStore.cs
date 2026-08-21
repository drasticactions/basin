using Basin.Capabilities;
using Wayland;
using Xkb;

namespace Basin.Seat;

public sealed class SeatSelectionStore : ISelectionStore
{
    private readonly Seat _seat;

    public SeatSelectionStore(Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _seat = seat;
        _seat.DataDevice.SelectionChanged += _ => SelectionChanged?.Invoke(SelectionKind.Clipboard);
        _seat.DataDevice.PrimarySelectionChanged += _ => SelectionChanged?.Invoke(SelectionKind.Primary);

        _seat.DataDevice.Store ??= this;
    }

    public event Action<SelectionKind>? SelectionChanged;

    public DataSource? Current(SelectionKind kind)
    {
        var source = kind == SelectionKind.Primary
            ? _seat.DataDevice.PrimarySelection
            : _seat.DataDevice.Selection;
        return source is { IsDestroyed: false } ? source : null;
    }

    public int GetOffer(SelectionKind kind, Span<string> types)
    {
        if (Current(kind) is not { } source)
        {
            return 0;
        }

        var mimes = source.MimeTypes;
        if (mimes.Count > types.Length)
        {
            return -1;
        }

        for (var i = 0; i < mimes.Count; i++)
        {
            types[i] = mimes[i];
        }

        return mimes.Count;
    }

    public bool SetSelection(SelectionKind kind, DataSource? source, uint serial)
    {
        if (serial != SelectionSerial.Unchecked && !_seat.ValidateSelectionSerial(serial))
        {
            return false;
        }

        if (kind == SelectionKind.Primary)
        {
            _seat.DataDevice.SetPrimarySelection(source);
        }
        else
        {
            _seat.DataDevice.SetSelection(source);
        }

        return true;
    }

    public bool Receive(SelectionKind kind, string mimeType, ClientFd fd)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        if (Current(kind) is not { } source)
        {
            fd.Close();
            return false;
        }

        source.Send(mimeType, fd);
        return true;
    }
}
