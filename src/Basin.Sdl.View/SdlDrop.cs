using System.Runtime.InteropServices;
using System.Text;
using Basin.Backend.Hosted;
using Basin.Hosted;
using SDL3;

namespace Basin.Sdl;

/// <summary>
/// Turns a drag from the host into a seat drag on one window. The drag offers a URI list and plain text, because SDL gives the data only at the drop.
/// </summary>
public sealed class SdlDrop : ISdlEventSink, IDisposable
{
    /// <summary>
    /// The MIME type that carries the dropped files as <c>file://</c> URIs, one per line.
    /// </summary>
    public const string UriListMime = "text/uri-list";

    private static readonly string[] Mimes = [UriListMime, "text/plain;charset=utf-8", "text/plain"];

    private readonly BasinCompositorHost _host;
    private readonly SdlLoop _loop;
    private readonly SdlOutputWindow _window;
    private readonly Action<FrameTick> _apply;
    private readonly List<(string Mime, ClientFd Fd)> _waiting = [];
    private readonly List<string> _files = [];
    private readonly StringBuilder _text = new();
    private DataSource? _source;
    private bool _beginPending;
    private bool _completePending;
    private byte[]? _uriList;
    private byte[]? _plain;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlDrop"/> class and adds it to the loop as an event sink.
    /// </summary>
    /// <param name="host">The host whose seat runs the drag.</param>
    /// <param name="loop">The SDL loop that delivers the drop events.</param>
    /// <param name="window">The window that receives drops. Drop events of other windows are ignored.</param>
    public SdlDrop(BasinCompositorHost host, SdlLoop loop, SdlOutputWindow window)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(window);
        _host = host;
        _loop = loop;
        _window = window;
        _apply = _ => Apply();
        host.Session.BeforeDispatch += _apply;
        loop.AddSink(this);
    }

    /// <summary>
    /// Gets a value indicating whether a host drag runs in the seat now.
    /// </summary>
    public bool IsDragging => _source is not null;

    /// <summary>
    /// Gets the number of drops that carried files or text.
    /// </summary>
    public long Drops { get; private set; }

    /// <summary>
    /// Records the drop events of the window. The drag starts and ends in the next dispatch, after the input of the window.
    /// </summary>
    /// <param name="e">The SDL event.</param>
    /// <returns><see langword="true"/> for a drop event of the window, else <see langword="false"/>.</returns>
    public bool Handle(in SDL.Event e)
    {
        var type = (SDL.EventType)e.Type;
        if (type is < SDL.EventType.DropFirst or > SDL.EventType.DropLast || e.Drop.WindowID != _window.Window.Id)
        {
            return false;
        }

        switch (type)
        {
            case SDL.EventType.DropBegin:
                Begin();
                return true;
            case SDL.EventType.DropPosition:
                Begin();
                var (x, y) = _window.ToLogical(e.Drop.X, e.Drop.Y);
                _window.Enqueue(new BasinViewInput(
                    BasinViewInputKind.PointerMotion, (uint)SDL.GetTicks(), x, y, 0, false, 0, 0, 0));
                return true;
            case SDL.EventType.DropFile:
                Begin();
                if (Marshal.PtrToStringUTF8(e.Drop.Data) is { Length: > 0 } path)
                {
                    _files.Add(path);
                }

                return true;
            case SDL.EventType.DropText:
                Begin();
                _text.Append(Marshal.PtrToStringUTF8(e.Drop.Data));
                return true;
            case SDL.EventType.DropComplete:
                _completePending = true;
                return true;
            default:
                return false;
        }
    }

    private void Begin()
    {
        if (_source is null && !_beginPending)
        {
            _beginPending = true;
            _uriList = null;
            _plain = null;
            _files.Clear();
            _text.Clear();
        }
    }

    private void Apply()
    {
        if (_beginPending)
        {
            _beginPending = false;
            var source = new DataSource([.. Mimes], Send, OnCancelled);
            _source = source;
            if (!_host.Seat.DataDevice.StartDrag(source))
            {
                _source = null;
            }
        }

        if (!_completePending)
        {
            return;
        }

        _completePending = false;
        if (_source is null)
        {
            return;
        }

        var dropped = _files.Count > 0 || _text.Length > 0;
        BuildPayload();
        foreach (var (mime, fd) in _waiting)
        {
            Deliver(mime, fd);
        }

        _waiting.Clear();
        _source = null;
        if (dropped)
        {
            Drops++;
        }

        _host.Seat.DataDevice.EndDrag(dropped ? Capabilities.DragOutcome.Dropped : Capabilities.DragOutcome.Cancelled);
    }

    private void BuildPayload()
    {
        if (_files.Count > 0)
        {
            var uris = new StringBuilder();
            var plain = new StringBuilder();
            foreach (var path in _files)
            {
                uris.Append(new Uri(Path.GetFullPath(path)).AbsoluteUri).Append("\r\n");
                plain.Append(path).Append('\n');
            }

            _uriList = Encoding.UTF8.GetBytes(uris.ToString());
            _plain = Encoding.UTF8.GetBytes(plain.ToString());
            return;
        }

        var text = _text.ToString();
        _plain = Encoding.UTF8.GetBytes(text);
        _uriList = Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            ? Encoding.UTF8.GetBytes(uri.AbsoluteUri + "\r\n")
            : [];
    }

    private void Send(string mime, ClientFd fd)
    {
        if (_plain is null)
        {
            _waiting.Add((mime, fd));
            return;
        }

        Deliver(mime, fd);
    }

    private void Deliver(string mime, ClientFd fd)
    {
        var bytes = (mime == UriListMime ? _uriList : _plain) ?? [];
        if (fd.Owner is { FdSlots: not null })
        {
            ChannelSelection.Write(fd, bytes);
            return;
        }

        _ = Task.Run(() =>
        {
            SdlPipes.WriteAll(fd.Value, bytes);
            fd.Close();
        });
    }

    private void OnCancelled()
    {
        foreach (var (_, fd) in _waiting)
        {
            fd.Close();
        }

        _waiting.Clear();
    }

    /// <summary>
    /// Leaves the loop, cancels a running drag and closes the descriptors that wait for data. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loop.RemoveSink(this);
        _host.Session.BeforeDispatch -= _apply;
        if (_source is not null)
        {
            _host.Seat.DataDevice.EndDrag(Capabilities.DragOutcome.Cancelled);
            _source = null;
        }

        OnCancelled();
    }
}
