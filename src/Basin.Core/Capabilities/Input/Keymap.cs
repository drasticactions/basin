namespace Basin.Capabilities;

public sealed class Keymap : IDisposable
{
    private readonly Dictionary<Wayland.Server.IFdSlotTable, Wayland.Server.Shm.IShmBlob> _minted = [];
    private readonly Wayland.Server.Shm.IShmBlobFactory? _blobs;
    private readonly byte[] _bytes;
    private Wayland.Server.Shm.IShmBlob? _blob;

    public Keymap(string text, Wayland.Server.Shm.IShmBlobFactory? blobs = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        var length = System.Text.Encoding.UTF8.GetByteCount(text);
        _bytes = new byte[length + 1];
        System.Text.Encoding.UTF8.GetBytes(text, _bytes);
        _blobs = blobs;
        Size = (uint)_bytes.Length;
    }

    public string Text { get; }

    public int Fd => HostBlob.FdSlot;

    public uint Size { get; }

    private Wayland.Server.Shm.IShmBlob HostBlob =>
        _blob ??= (_blobs ?? Wayland.Server.Shm.ShmBlobs.ForFdSlots(null)).Create("basin-keymap", _bytes);

    public int FdFor(Wayland.Server.WlClient? client)
    {
        if (client?.FdSlots is not { } slots)
        {
            return HostBlob.FdSlot;
        }

        if (!_minted.TryGetValue(slots, out var blob))
        {
            blob = Wayland.Server.Shm.ShmBlobs.ForFdSlots(slots).Create("basin-keymap", _bytes);
            _minted[slots] = blob;
        }

        return blob.FdSlot;
    }

    public void Dispose()
    {
        foreach (var blob in _minted.Values)
        {
            blob.Dispose();
        }

        _minted.Clear();
        _blob?.Dispose();
        _blob = null;
    }
}
