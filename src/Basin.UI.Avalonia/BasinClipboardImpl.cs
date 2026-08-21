using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Basin.Capabilities;

namespace Basin.UI.Avalonia;

internal sealed class BasinClipboardImpl : IClipboardImpl
{
    private static readonly string[] TextMimeTypes =
    [
        "text/plain;charset=utf-8",
        "text/plain",
        "UTF8_STRING",
        "STRING",
        "TEXT",
    ];

    private readonly ISelectionStore? _store;
    private readonly ICompositorEventLoop? _loop;

    public BasinClipboardImpl(ISelectionStore? store, ICompositorEventLoop? loop)
    {
        _store = store;
        _loop = loop;
    }

    public Task<IAsyncDataTransfer?> TryGetDataAsync()
    {
        if (_store is null || _loop is null)
        {
            return Task.FromResult<IAsyncDataTransfer?>(null);
        }

        var types = new string[16];
        var count = _store.GetOffer(SelectionKind.Clipboard, types);
        if (count <= 0)
        {
            return Task.FromResult<IAsyncDataTransfer?>(null);
        }

        string? chosen = null;
        for (var i = 0; i < TextMimeTypes.Length && chosen is null; i++)
        {
            for (var j = 0; j < count && j < types.Length; j++)
            {
                if (string.Equals(types[j], TextMimeTypes[i], StringComparison.OrdinalIgnoreCase))
                {
                    chosen = types[j];
                    break;
                }
            }
        }

        if (chosen is null)
        {
            return Task.FromResult<IAsyncDataTransfer?>(null);
        }

        return ReadAsync(chosen);
    }

    public Task SetDataAsync(IAsyncDataTransfer dataTransfer)
    {
        if (_store is null)
        {
            return Task.CompletedTask;
        }

        return SetAsync(dataTransfer);
    }

    public Task ClearAsync()
    {
        _store?.SetSelection(SelectionKind.Clipboard, null, SelectionSerial.Unchecked);
        return Task.CompletedTask;
    }

    private async Task SetAsync(IAsyncDataTransfer dataTransfer)
    {
        string? text = null;
        foreach (var item in dataTransfer.Items)
        {
            if (item.Formats.Contains(DataFormat.Text) &&
                await item.TryGetRawAsync(DataFormat.Text).ConfigureAwait(true) is string value)
            {
                text = value;
                break;
            }
        }

        if (text is null)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var source = new DataSource(
            [.. TextMimeTypes],
            (_, fd) =>
            {
                Write(fd.Value, bytes);
                fd.Close();
            });

        _store!.SetSelection(SelectionKind.Clipboard, source, SelectionSerial.Unchecked);
    }

    private Task<IAsyncDataTransfer?> ReadAsync(string mimeType)
    {
        Span<int> fds = stackalloc int[2];
        if (Pipe(fds, O_CLOEXEC | O_NONBLOCK) != 0)
        {
            return Task.FromResult<IAsyncDataTransfer?>(null);
        }

        var readFd = fds[0];
        var writeFd = fds[1];
        if (!_store!.Receive(SelectionKind.Clipboard, mimeType, new ClientFd(writeFd, null)))
        {
            close(readFd);
            close(writeFd);
            return Task.FromResult<IAsyncDataTransfer?>(null);
        }

        close(writeFd);

        var completion = new TaskCompletionSource<IAsyncDataTransfer?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var buffer = new MemoryStream();
        IEventSource? source = null;
        source = _loop!.AddFd(readFd, FdReadiness.Readable, (fd, _) =>
        {
            Span<byte> chunk = stackalloc byte[4096];
            while (true)
            {
                var read = Read(fd, chunk);
                if (read > 0)
                {
                    buffer.Write(chunk[..read]);
                    continue;
                }

                if (read < 0 && Marshal.GetLastPInvokeError() == EAGAIN)
                {
                    return;
                }

                source!.Remove();
                close(fd);
                var text = Encoding.UTF8.GetString(buffer.ToArray());
                completion.TrySetResult(new TextTransfer(text));
                return;
            }
        });

        return completion.Task;
    }

    private static void Write(int fd, byte[] bytes)
    {
        var written = 0;
        while (written < bytes.Length)
        {
            var count = Write(fd, bytes.AsSpan(written));
            if (count <= 0)
            {
                return;
            }

            written += count;
        }
    }

    private const int O_CLOEXEC = 0x80000;
    private const int O_NONBLOCK = 0x800;
    private const int EAGAIN = 11;

    [DllImport("libc", EntryPoint = "pipe2", SetLastError = true)]
    private static extern int pipe2(ref int fds, int flags);

    private static int Pipe(Span<int> fds, int flags) =>
        pipe2(ref MemoryMarshal.GetReference(fds), flags);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint read(int fd, ref byte buffer, nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern nint write(int fd, ref byte buffer, nuint count);

    private static int Read(int fd, Span<byte> buffer) =>
        (int)read(fd, ref MemoryMarshal.GetReference(buffer), (nuint)buffer.Length);

    private static int Write(int fd, ReadOnlySpan<byte> buffer) =>
        (int)write(fd, ref MemoryMarshal.GetReference(buffer), (nuint)buffer.Length);

    private sealed class TextTransfer : IAsyncDataTransfer, IAsyncDataTransferItem
    {
        private readonly string _text;

        public TextTransfer(string text) => _text = text;

        public IReadOnlyList<DataFormat> Formats => [DataFormat.Text];

        public IReadOnlyList<IAsyncDataTransferItem> Items => [this];

        public Task<object?> TryGetRawAsync(DataFormat format) =>
            Task.FromResult<object?>(format == DataFormat.Text ? _text : null);

        public void Dispose()
        {
        }
    }
}
