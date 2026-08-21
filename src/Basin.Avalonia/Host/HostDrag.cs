using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;
using Basin.Seat;

namespace Basin.Avalonia;

public sealed class HostDrag : IDisposable
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc")]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);

    private readonly BasinCompositorHost _host;
    private DataSource? _hostSource;
    private volatile string? _clientDragText;
    private volatile bool _clientDragActive;
    private bool _disposed;

    public HostDrag(BasinCompositorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        host.Seat.DataDevice.DragStarted += OnDragStarted;
        host.Seat.DataDevice.DragEnded += OnDragEnded;
    }

    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(4);

    public event Action<Surface?>? ClientDragStarted;

    public event Action? ClientDragEnded;

    public bool ClientDragActive => _clientDragActive;

    public string? TakeClientDragText() => _clientDragText;

    public void EnterFromHost(Surface surface, double x, double y, IReadOnlyList<(string Mime, byte[] Data)> payload)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (_disposed || payload.Count == 0)
        {
            return;
        }

        var mimes = new List<string>(payload.Count);
        foreach (var (mime, _) in payload)
        {
            mimes.Add(mime);
        }

        var data = payload;
        var source = new DataSource(mimes, (mime, fd) =>
        {
            foreach (var (offered, bytes) in data)
            {
                if (offered == mime)
                {
                    if (fd.Owner is { FdSlots: not null })
                    {
                        ChannelSelection.Write(fd, bytes);
                    }
                    else
                    {
                        _ = Task.Run(() => WriteAll(fd, bytes));
                    }

                    return;
                }
            }

            fd.Close();
        });
        _hostSource = source;
        _host.Seat.DataDevice.StartDrag(source);
        MotionFromHost(surface, x, y);
    }

    public void MotionFromHost(Surface surface, double x, double y)
    {
        if (_hostSource is not null)
        {
            _host.Seat.Pointer.NotifyMotionAt((uint)Environment.TickCount, surface, x, y, x, y);
        }
    }

    public void DropFromHost()
    {
        if (_hostSource is not null)
        {
            _host.Seat.DataDevice.EndDrag(Capabilities.DragOutcome.Dropped);
            _hostSource = null;
        }
    }

    public void LeaveFromHost()
    {
        if (_hostSource is not null)
        {
            _host.Seat.DataDevice.EndDrag(Capabilities.DragOutcome.Cancelled);
            _hostSource = null;
        }
    }

    public void EndClientDrag(bool dropped)
    {
        if (_clientDragActive)
        {
            _host.Seat.DataDevice.EndDrag(
                dropped ? Capabilities.DragOutcome.Dropped : Capabilities.DragOutcome.Cancelled);
        }
    }

    private void OnDragStarted(DragEvent drag)
    {
        if (drag.Source is not { Resource: not null } source)
        {
            return;
        }

        _clientDragActive = true;
        _clientDragText = null;
        ClientDragStarted?.Invoke(drag.Icon);

        string? mime = null;
        foreach (var offered in source.MimeTypes)
        {
            if (offered.StartsWith("text/", StringComparison.Ordinal))
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
            var inbound = new global::Basin.PipeFromClient();
            source.Send(mime, new global::Basin.ClientFd(slots.Mint(inbound), remote));
            _ = Task.Run(() =>
            {
                if (inbound.ReadToEnd(timeout) is { } bytes)
                {
                    _clientDragText = Encoding.UTF8.GetString(bytes);
                }
            });
            return;
        }

        int readFd, writeFd;
        unsafe
        {
            var fds = stackalloc int[2];
            if (pipe2(fds, 0) != 0)
            {
                return;
            }

            readFd = fds[0];
            writeFd = fds[1];
        }

        source.Send(mime, new global::Basin.ClientFd(writeFd, source.Resource?.Client));
        _ = Task.Run(() =>
        {
            var text = ReadAllText(readFd, timeout);
            if (text is not null)
            {
                _clientDragText = text;
            }
        });
    }

    private void OnDragEnded()
    {
        _clientDragActive = false;
        _hostSource = null;
        ClientDragEnded?.Invoke();
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

            BasinLog.Warn($"avalonia: a drag source stalled; the host drag carries nothing");
            return null;
        }
        finally
        {
            close(fd);
        }
    }

    private static void WriteAll(global::Basin.ClientFd fd, byte[] bytes)
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
        _host.Seat.DataDevice.DragStarted -= OnDragStarted;
        _host.Seat.DataDevice.DragEnded -= OnDragEnded;
    }
}
