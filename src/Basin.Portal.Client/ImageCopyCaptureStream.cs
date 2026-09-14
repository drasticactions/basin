using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Protocol;
using Basin.Portal.Client.Protocol;
using Pixman;
using System.Runtime.InteropServices;
using Wayland;

namespace Basin.Portal.Client;

internal sealed class ImageCopyCaptureStream : IDisposable
{
    private readonly ImageCopyCaptureClient _owner;
    private readonly WlDisplay _display;
    private readonly ClientShm _shm;
    private readonly ExtImageCaptureSourceV1 _source;
    private readonly IOutput? _output;
    private readonly Dictionary<nint, WlBuffer> _dmabufWraps = [];
    private ExtImageCopyCaptureSessionV1? _session;
    private ExtImageCopyCaptureFrameV1? _frame;
    private bool _sizeKnown;
    private bool _paintCursors;
    private bool _frameOk;
    private bool _disposed;

    public ImageCopyCaptureStream(ImageCopyCaptureClient owner, WlShm shm, WlDisplay display, ExtImageCaptureSourceV1 source, IOutput? output)
    {
        _owner = owner;
        _display = display;
        _shm = new ClientShm(shm);
        _source = source;
        _output = output;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public IOutput? Output => _output;

    public DrmFormatSet Constraints { get; } = new();

    public bool TryDescribe(bool paintCursors, out CaptureFormat format)
    {
        Configure(paintCursors);
        var deadline = Environment.TickCount64 + 1000;
        while (!_sizeKnown && Environment.TickCount64 < deadline)
        {
            if (!Pump(200))
            {
                break;
            }
        }

        format = new CaptureFormat(Width, Height, DrmFormat.Xrgb8888);
        return _sizeKnown && Width > 0 && Height > 0;
    }

    public bool CaptureInto(IBuffer target)
    {
        if (!_sizeKnown && !TryDescribe(_paintCursors, out _))
        {
            return false;
        }

        if (target.TryGetDmabuf(out var attributes))
        {
            return Wrap(target, in attributes) is { } wire && CaptureFrame(wire, copyInto: null);
        }

        return _shm.Buffer is { } shm && CaptureFrame(shm, copyInto: target);
    }

    public void Dispose()
    {
        _disposed = true;
        DestroyFrame();
        foreach (var wrap in _dmabufWraps.Values)
        {
            if (!wrap.IsDestroyed)
            {
                wrap.Dispose();
            }
        }

        _dmabufWraps.Clear();
        if (_session is { IsDestroyed: false } session)
        {
            session.Destroy();
        }

        if (!_source.IsDestroyed)
        {
            _source.Destroy();
        }

        _shm.Dispose();
    }

    private void Configure(bool paintCursors)
    {
        if (_session is not null && _paintCursors == paintCursors)
        {
            return;
        }

        _paintCursors = paintCursors;
        DestroyFrame();
        if (_session is { IsDestroyed: false } old)
        {
            old.Destroy();
        }

        _sizeKnown = false;
        var options = paintCursors ? ExtImageCopyCaptureManagerV1.Options.PaintCursors : 0;
        var session = _owner.Manager.CreateSession(_source, options);
        _session = session;
        session.BufferSize += (_, e) =>
        {
            Width = (int)e.Width;
            Height = (int)e.Height;
        };
        session.DmabufFormat += (_, e) =>
        {
            var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(e.Modifiers);
            Constraints.Add((DrmFormat)e.Format, span);
            _owner.OnConstraints(Constraints);
        };
        session.Done += (_, _) =>
        {
            _sizeKnown = true;
            if (Width > 0 && Height > 0)
            {
                _shm.Resize(Width, Height);
            }
        };
        session.Stopped += (_, _) => _sizeKnown = false;
    }

    private WlBuffer? Wrap(IBuffer target, in DmabufAttributes attributes)
    {
        if (_owner.Dmabuf is not { } dmabuf)
        {
            return null;
        }

        var key = (nint)target.GetHashCode();
        if (_dmabufWraps.TryGetValue(key, out var existing) && !existing.IsDestroyed)
        {
            return existing;
        }

        var parameters = dmabuf.CreateParams();
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            parameters.Add(
                attributes.Fds[plane],
                (uint)plane,
                attributes.Offsets[plane],
                attributes.Strides[plane],
                (uint)(attributes.Modifier >> 32),
                (uint)attributes.Modifier);
        }

        var wire = parameters.CreateImmed(attributes.Width, attributes.Height, (uint)attributes.Format, 0);
        parameters.Dispose();
        _dmabufWraps[key] = wire;
        return wire;
    }

    private bool CaptureFrame(WlBuffer attach, IBuffer? copyInto)
    {
        if (_disposed || _session is not { IsDestroyed: false } session)
        {
            return false;
        }

        DestroyFrame();
        _frameOk = false;
        var frame = session.CreateFrame();
        _frame = frame;
        frame.AttachBuffer(attach);
        frame.DamageBuffer(0, 0, Width, Height);
        frame.Ready += (_, _) => OnReady();
        frame.Failed += (_, e) => OnFailed(e.Reason);
        frame.Capture();
        _display.Flush();
        var deadline = Environment.TickCount64 + 1000;
        while (_frame is not null && Environment.TickCount64 < deadline)
        {
            if (!Pump(200))
            {
                break;
            }
        }

        if (!_frameOk)
        {
            return false;
        }

        return copyInto is null || Copy(copyInto);
    }

    private void OnReady()
    {
        _frameOk = true;
        DestroyFrame();
        _owner.NotifyDamaged(_output, new Box(0, 0, Width, Height));
    }

    private void OnFailed(ExtImageCopyCaptureFrameV1.FailureReason reason)
    {
        DestroyFrame();
        if (reason == ExtImageCopyCaptureFrameV1.FailureReason.BufferConstraints)
        {
            _sizeKnown = false;
        }
    }

    private unsafe bool Copy(IBuffer target)
    {
        if (_shm.Data == 0 || target.Width < Width || target.Height < Height)
        {
            return false;
        }

        if (!target.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return false;
        }

        try
        {
            var rows = Math.Min(Height, target.Height);
            var bytes = Math.Min(_shm.Stride, view.Stride);
            for (var y = 0; y < rows; y++)
            {
                new ReadOnlySpan<byte>((byte*)_shm.Data + (y * _shm.Stride), bytes)
                    .CopyTo(new Span<byte>((byte*)view.Data + (y * view.Stride), bytes));
            }
        }
        finally
        {
            target.EndDataAccess();
        }

        return true;
    }

    private bool Pump(int timeoutMs)
    {
        try
        {
            _display.Flush();
        }
        catch (WaylandException)
        {
            return false;
        }

        var fds = new PollFd { fd = _display.Fd, events = 0x001 };
        var ready = poll(ref fds, 1, timeoutMs);
        if (ready < 0)
        {
            return false;
        }

        if (ready == 0)
        {
            return true;
        }

        try
        {
            _display.Dispatch();
        }
        catch (WaylandException)
        {
            return false;
        }

        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [DllImport("libc")]
    private static extern int poll(ref PollFd fds, uint nfds, int timeout);

    private void DestroyFrame()
    {
        if (_frame is { IsDestroyed: false } frame)
        {
            frame.Destroy();
        }

        _frame = null;
    }
}
