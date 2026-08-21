using Basin.Backend.Wayland.Protocol;
using Basin.Capabilities;
using Wayland;

namespace Basin.Backend.Wayland;

internal abstract class WaylandSeamSelectionBridge : IDisposable
{
    private readonly ISelectionStore _store;
    private readonly SelectionKind _kind;
    private string[] _mimeTypeBuffer = new string[16];
    private DataSource? _injected;
    private bool _settingFromParent;
    private bool _disposed;

    protected WaylandSeamSelectionBridge(WaylandBackend backend, ISelectionStore store, SelectionKind kind)
    {
        Backend = backend;
        _store = store;
        _kind = kind;
        _store.SelectionChanged += OnGuestSelectionChanged;
    }

    protected WaylandBackend Backend { get; }

    protected bool IsDisposed => _disposed;

    public virtual void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.SelectionChanged -= OnGuestSelectionChanged;
        RetractFromGuests();
    }

    protected void OfferToGuests(List<string> mimeTypes)
    {
        RetractFromGuests();
        var source = new DataSource(mimeTypes, SendFromParent, OnGuestsTookSelection);
        _injected = source;
        _settingFromParent = true;
        try
        {
            _store.SetSelection(_kind, source, SelectionSerial.Unchecked);
        }
        finally
        {
            _settingFromParent = false;
        }
    }

    protected void RetractFromGuests()
    {
        var source = _injected;
        _injected = null;
        source?.MarkDestroyed();
    }

    protected void ReceiveForParent(string mimeType, int fd) =>
        _store.Receive(_kind, mimeType, new ClientFd(fd, null));

    protected abstract void SendFromParent(string mimeType, ClientFd fd);

    protected abstract void PushToParent(ReadOnlySpan<string> mimeTypes, uint serial);

    protected abstract void DropParentSource();

    private void OnGuestsTookSelection() => _injected = null;

    private void OnGuestSelectionChanged(SelectionKind kind)
    {
        if (_disposed || kind != _kind || _settingFromParent)
        {
            return;
        }

        var current = _store.Current(_kind);
        if (current is null)
        {
            DropParentSource();
            return;
        }

        if (ReferenceEquals(current, _injected))
        {
            return;
        }

        int count;
        while ((count = _store.GetOffer(_kind, _mimeTypeBuffer)) < 0)
        {
            _mimeTypeBuffer = new string[_mimeTypeBuffer.Length * 2];
        }

        if (count == 0)
        {
            DropParentSource();
            return;
        }

        PushToParent(_mimeTypeBuffer.AsSpan(0, count), Backend.LatestInputSerial);
    }
}
