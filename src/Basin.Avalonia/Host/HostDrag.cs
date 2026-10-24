using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;
using Basin.Seat;
using Basin.Hosted;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

/// <summary>
/// Connects drag and drop between the host desktop and the compositor clients. It carries text from client drags and any data into host drags.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="HostDrag"/> class and starts to watch client drags on the seat.
    /// </summary>
    /// <param name="host">The compositor host.</param>
    public HostDrag(BasinCompositorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        host.Seat.DataDevice.DragStarted += OnDragStarted;
        host.Seat.DataDevice.DragEnded += OnDragEnded;
    }

    /// <summary>
    /// Gets or sets the longest time to wait for a client to write the text of its drag. The default is 4 seconds.
    /// </summary>
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Occurs on the compositor thread when a client starts a drag, with the drag icon surface or <see langword="null"/>.
    /// </summary>
    public event Action<Surface?>? ClientDragStarted;

    /// <summary>
    /// Occurs on the compositor thread when any drag ends.
    /// </summary>
    public event Action? ClientDragEnded;

    /// <summary>
    /// Gets a value indicating whether a client drag is in progress. Any thread can read it.
    /// </summary>
    public bool ClientDragActive => _clientDragActive;

    /// <summary>
    /// Gets the text of the current client drag and clears it, so a drag hands off once. Any thread can call it.
    /// </summary>
    /// <returns>The text, or <see langword="null"/> when the client offered no text type, did not finish the write, or the text was taken.</returns>
    public string? TakeClientDragText() => Interlocked.Exchange(ref _clientDragText, null);

    /// <summary>
    /// Starts a compositor drag for data that the host drags onto a surface. An empty payload does nothing.
    /// </summary>
    /// <param name="surface">The surface under the host pointer.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <param name="payload">The data of the host drag, one entry per MIME type.</param>
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

    /// <summary>
    /// Moves the pointer of a host drag. It does nothing without a host drag.
    /// </summary>
    /// <param name="surface">The surface under the host pointer.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    public void MotionFromHost(Surface surface, double x, double y)
    {
        if (_hostSource is not null)
        {
            _host.Seat.Pointer.NotifyMotionAt((uint)Environment.TickCount, surface, x, y, x, y);
        }
    }

    /// <summary>
    /// Drops the host drag on the surface under the pointer. It does nothing without a host drag.
    /// </summary>
    public void DropFromHost()
    {
        if (_hostSource is not null)
        {
            _host.Seat.DataDevice.EndDrag(Capabilities.DragOutcome.Dropped);
            _hostSource = null;
        }
    }

    /// <summary>
    /// Cancels the host drag after the host pointer left. It does nothing without a host drag.
    /// </summary>
    public void LeaveFromHost()
    {
        if (_hostSource is not null)
        {
            _host.Seat.DataDevice.EndDrag(Capabilities.DragOutcome.Cancelled);
            _hostSource = null;
        }
    }

    /// <summary>
    /// Ends the current client drag after it left for the host. It does nothing without a client drag.
    /// </summary>
    /// <param name="dropped"><see langword="true"/> when the host accepted the drop, <see langword="false"/> to cancel.</param>
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

            Log.Warn($"a drag source stalled; the host drag carries nothing");
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

    /// <summary>
    /// Stops watching client drags. A second call does nothing.
    /// </summary>
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
