using System.Runtime.InteropServices;
using Basin.Capabilities;

namespace Basin.UI.ImGui;

public sealed unsafe class ImGuiClipboard : IDisposable
{
    private static readonly string[] TextMimeTypes =
    [
        "text/plain;charset=utf-8",
        "text/plain",
        "UTF8_STRING",
        "STRING",
        "TEXT",
    ];

    private const int O_CLOEXEC = 0x80000;
    private const int O_NONBLOCK = 0x800;
    private const int EAGAIN = 11;
    private const int EINTR = 4;

    private readonly ISelectionStore _store;
    private readonly ICompositorEventLoop _loop;
    private readonly string[] _offer = new string[32];
    private DataSource? _owned;
    private IEventSource? _reading;
    private int _readFd = -1;
    private MemoryStream? _buffer;
    private byte* _text;
    private int _capacity;
    private int _length;
    private bool _disposed;

    public ImGuiClipboard(ISelectionStore store, ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(loop);

        _store = store;
        _loop = loop;
        Store(ReadOnlySpan<byte>.Empty);
        _store.SelectionChanged += OnSelectionChanged;
        Refresh();
    }

    public ReadOnlySpan<byte> Text => _text is null ? default : new ReadOnlySpan<byte>(_text, _length);

    internal byte* NativeText => _text;

    public void SetText(ReadOnlySpan<byte> utf8)
    {
        if (_disposed)
        {
            return;
        }

        var bytes = utf8.ToArray();
        var source = new DataSource(
            [.. TextMimeTypes],
            (_, fd) =>
            {
                Write(fd.Value, bytes);
                fd.Close();
            });
        _owned = source;
        Store(bytes);
        StopReading();
        _store.SetSelection(SelectionKind.Clipboard, source, SelectionSerial.Unchecked);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.SelectionChanged -= OnSelectionChanged;
        StopReading();
        NativeMemory.Free(_text);
        _text = null;
        _length = 0;
        _capacity = 0;
    }

    private void Store(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length + 1 > _capacity)
        {
            NativeMemory.Free(_text);
            _capacity = Math.Max(64, utf8.Length + 1);
            _text = (byte*)NativeMemory.Alloc((nuint)_capacity);
        }

        utf8.CopyTo(new Span<byte>(_text, _capacity));
        _text[utf8.Length] = 0;
        _length = utf8.Length;
    }

    private void OnSelectionChanged(SelectionKind kind)
    {
        if (kind == SelectionKind.Clipboard && !_disposed)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_owned is { } owned && ReferenceEquals(_store.Current(SelectionKind.Clipboard), owned))
        {
            return;
        }

        _owned = null;
        StopReading();
        var count = _store.GetOffer(SelectionKind.Clipboard, _offer);
        if (count <= 0)
        {
            Store(ReadOnlySpan<byte>.Empty);
            return;
        }

        string? chosen = null;
        for (var i = 0; i < TextMimeTypes.Length && chosen is null; i++)
        {
            for (var j = 0; j < count && j < _offer.Length; j++)
            {
                if (string.Equals(_offer[j], TextMimeTypes[i], StringComparison.OrdinalIgnoreCase))
                {
                    chosen = _offer[j];
                    break;
                }
            }
        }

        if (chosen is null)
        {
            Store(ReadOnlySpan<byte>.Empty);
            return;
        }

        Span<int> fds = stackalloc int[2];
        if (pipe2(ref MemoryMarshal.GetReference(fds), O_CLOEXEC | O_NONBLOCK) != 0)
        {
            return;
        }

        if (!_store.Receive(SelectionKind.Clipboard, chosen, new ClientFd(fds[1], null)))
        {
            close(fds[0]);
            return;
        }

        _readFd = fds[0];
        _buffer = new MemoryStream();
        _reading = _loop.AddFd(_readFd, FdReadiness.Readable, OnReadable);
    }

    private void OnReadable(int fd, FdReadiness readiness)
    {
        Span<byte> chunk = stackalloc byte[4096];
        while (true)
        {
            var count = (int)Read(fd, ref MemoryMarshal.GetReference(chunk), (nuint)chunk.Length);
            if (count > 0)
            {
                _buffer!.Write(chunk[..count]);
                continue;
            }

            if (count < 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == EINTR)
                {
                    continue;
                }

                if (error == EAGAIN)
                {
                    return;
                }
            }

            if (count == 0 && _buffer is { } buffer)
            {
                var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
                var terminator = bytes.IndexOf((byte)0);
                Store(terminator < 0 ? bytes : bytes[..terminator]);
            }

            StopReading();
            return;
        }
    }

    private void StopReading()
    {
        _reading?.Remove();
        _reading = null;
        if (_readFd >= 0)
        {
            close(_readFd);
            _readFd = -1;
        }

        _buffer?.Dispose();
        _buffer = null;
    }

    private static void Write(int fd, byte[] bytes)
    {
        if (fd < 0)
        {
            return;
        }

        var written = 0;
        while (written < bytes.Length)
        {
            var count = (int)Write(fd, ref bytes[written], (nuint)(bytes.Length - written));
            if (count <= 0)
            {
                return;
            }

            written += count;
        }
    }

    [DllImport("libc", EntryPoint = "pipe2", SetLastError = true)]
    private static extern int pipe2(ref int fds, int flags);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint Read(int fd, ref byte buffer, nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern nint Write(int fd, ref byte buffer, nuint count);
}
