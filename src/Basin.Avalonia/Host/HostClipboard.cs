using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

public sealed class HostClipboard : IDisposable
{
    private const string TextMime = "text/plain;charset=utf-8";
    private const string TextMimePlain = "text/plain";

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc")]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);

    private readonly ISelectionStore _store;
    private readonly Func<IClipboard?> _hostClipboard;
    private readonly Action<Action> _post;
    private DataSource? _ownSource;
    private string? _lastText;
    private bool _disposed;

    public HostClipboard(BasinCompositorHost host, Func<IClipboard?> hostClipboard, Action<Action> postToCompositor)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(hostClipboard);
        ArgumentNullException.ThrowIfNull(postToCompositor);
        _hostClipboard = hostClipboard;
        _post = postToCompositor;
        _store = host.Services.Require<ISelectionStore>();
        _store.SelectionChanged += OnSelectionChanged;
    }

    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(4);

    private void OnSelectionChanged(SelectionKind kind)
    {
        if (_disposed || kind != SelectionKind.Clipboard)
        {
            return;
        }

        var source = _store.Current(kind);
        if (source is null || ReferenceEquals(source, _ownSource))
        {
            return;
        }

        string? mime = null;
        foreach (var offered in source.MimeTypes)
        {
            if (offered.StartsWith(TextMimePlain, StringComparison.Ordinal))
            {
                mime = offered;
                break;
            }
        }

        if (mime is null)
        {
            return;
        }

        var timeout = ReadTimeout;
        if (source.Client is { FdSlots: { } slots } remote)
        {
            var inbound = new PipeFromClient();
            if (!_store.Receive(kind, mime, new ClientFd(slots.Mint(inbound), remote)))
            {
                return;
            }

            Log.Debug($"clipboard: mirroring '{mime}' to the host over the client's channel");
            _ = Task.Run(() => Mirror(Decode(inbound.ReadToEnd(timeout))));
            return;
        }

        int readFd, writeFd;
        unsafe
        {
            var fds = stackalloc int[2];
            if (pipe(fds) != 0)
            {
                return;
            }

            readFd = fds[0];
            writeFd = fds[1];
        }

        if (!_store.Receive(kind, mime, new ClientFd(writeFd, source.Resource?.Client)))
        {
            close(readFd);
            close(writeFd);
            return;
        }

        Log.Debug($"clipboard: mirroring '{mime}' to the host");
        _ = Task.Run(() => Mirror(ReadAllText(readFd, timeout)));
    }

    private static string? Decode(byte[]? bytes) => bytes is null ? null : Encoding.UTF8.GetString(bytes);

    private void Mirror(string? text)
    {
        if (text is null)
        {
            Log.Debug($"clipboard: the client source produced nothing");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _lastText = text;
            var clipboard = _hostClipboard();
            Log.Debug($"clipboard: {text.Length} bytes to the host clipboard ({(clipboard is null ? "absent" : "present")})");
            _ = clipboard?.SetTextAsync(text);
        });
    }

    public async Task PushFromHostAsync()
    {
        string? text = null;
        if (_hostClipboard() is { } clipboard)
        {
            text = await clipboard.TryGetTextAsync();
        }

        if (string.IsNullOrEmpty(text) || text == _lastText)
        {
            return;
        }

        _lastText = text;
        var bytes = Encoding.UTF8.GetBytes(text);
        _post(() =>
        {
            if (_disposed)
            {
                return;
            }

            var source = new DataSource(
                [TextMime, TextMimePlain],
                (mime, fd) =>
                {
                    if (fd.Owner is { FdSlots: not null })
                    {
                        ChannelSelection.Write(fd, bytes);
                        return;
                    }

                    _ = Task.Run(() => WriteAll(fd, bytes));
                });
            _ownSource = source;
            if (!_store.SetSelection(SelectionKind.Clipboard, source, SelectionSerial.Unchecked))
            {
                _ownSource = null;
            }
        });
    }

    private static string? ReadAllText(int fd, TimeSpan timeout)
    {
        try
        {
            using var stream = new MemoryStream();
            var buffer = new byte[4096];
            var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                nint got;
                unsafe
                {
                    fixed (byte* data = buffer)
                    {
                        got = read(fd, data, (nuint)buffer.Length);
                    }
                }

                if (got < 0)
                {
                    return null;
                }

                if (got == 0)
                {
                    return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
                }

                stream.Write(buffer, 0, (int)got);
            }

            Log.Warn($"clipboard source stalled; the host clipboard keeps its old contents");
            return null;
        }
        finally
        {
            close(fd);
        }
    }

    private static void WriteAll(ClientFd fd, byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            nint wrote;
            unsafe
            {
                fixed (byte* data = bytes)
                {
                    wrote = write(fd.Value, data + offset, (nuint)(bytes.Length - offset));
                }
            }

            if (wrote <= 0)
            {
                break;
            }

            offset += (int)wrote;
        }

        fd.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.SelectionChanged -= OnSelectionChanged;
    }
}
