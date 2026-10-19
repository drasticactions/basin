using System.Runtime.InteropServices;
using System.Text;
using Basin.Backend.Hosted;
using Basin.Hosted;
using SDL3;

namespace Basin.Sdl;

public sealed class SdlDrop : ISdlEventSink, IDisposable
{
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

    public bool IsDragging => _source is not null;

    public long Drops { get; private set; }

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
