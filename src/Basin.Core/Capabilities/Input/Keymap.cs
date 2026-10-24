namespace Basin.Capabilities;

/// <summary>
/// A compiled XKB text keymap and the shared memory copies that clients receive through <c>wl_keyboard.keymap</c>.
/// </summary>
public sealed class Keymap : IDisposable
{
    private readonly Dictionary<Wayland.Server.IFdSlotTable, Wayland.Server.Shm.IShmBlob> _minted = [];
    private readonly Wayland.Server.Shm.IShmBlobFactory? _blobs;
    private readonly byte[] _bytes;
    private Wayland.Server.Shm.IShmBlob? _blob;

    /// <summary>
    /// Initializes a new instance of the <see cref="Keymap"/> class. The shared memory copy is created on first use.
    /// </summary>
    /// <param name="text">The keymap in the XKB text format.</param>
    /// <param name="blobs">The factory for the host copy, or <see langword="null"/> for the default factory.</param>
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

    /// <summary>Gets the keymap in the XKB text format.</summary>
    public string Text { get; }

    /// <summary>Gets the file descriptor slot of the host copy of the keymap. The first read creates the copy.</summary>
    public int Fd => HostBlob.FdSlot;

    /// <summary>Gets the size in bytes of the keymap copy. It includes the terminating null byte.</summary>
    public uint Size { get; }

    private Wayland.Server.Shm.IShmBlob HostBlob =>
        _blob ??= (_blobs ?? Wayland.Server.Shm.ShmBlobs.ForFdSlots(null)).Create("basin-keymap", _bytes);

    /// <summary>
    /// Gets the file descriptor slot of the keymap copy for a client. Clients that share a slot table share one copy.
    /// </summary>
    /// <param name="client">The client, or <see langword="null"/> for the host copy.</param>
    /// <returns>The slot of the copy. A client without a slot table gets the host copy.</returns>
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

    /// <summary>Closes all shared memory copies of the keymap.</summary>
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
