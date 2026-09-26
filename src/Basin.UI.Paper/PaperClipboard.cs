using System.Runtime.InteropServices;
using System.Text;
using Basin.Capabilities;
using Prowl.PaperUI;

namespace Basin.UI.Paper;

public sealed class PaperClipboard : IClipboardHandler, IDisposable
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
    private string _text = string.Empty;
    private bool _disposed;

    public PaperClipboard(ISelectionStore store, ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(loop);

        _store = store;
        _loop = loop;
        _store.SelectionChanged += OnSelectionChanged;
        Refresh();
    }

    public string Text => _text;

    public string GetClipboardText() => _text;

    public void SetClipboardText(string text)
    {
        if (_disposed)
        {
            return;
        }

        text ??= string.Empty;
        var bytes = Encoding.UTF8.GetBytes(text);
        var source = new DataSource(
            [.. TextMimeTypes],
            (_, fd) =>
            {
                Write(fd.Value, bytes);
                fd.Close();
            });
        _owned = source;
        _text = text;
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
            _text = string.Empty;
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
            _text = string.Empty;
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
            close(fds[1]);
            return;
        }

        close(fds[1]);
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
                _text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
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
