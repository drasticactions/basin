using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ImageCopyCaptureManager : ICaptureDamageObserver, IDisposable
{
    public const int Version = 1;

    private const uint ErrorInvalidOption = 1;
    private const uint ErrorDuplicateFrame = 1;
    private const uint ErrorNoBuffer = 1;
    private const uint ErrorInvalidBufferDamage = 2;
    private const uint ErrorAlreadyCaptured = 3;
    private const uint ErrorDuplicateSession = 1;

    private readonly WlGlobal _global;
    private readonly ClientBufferRegistry _buffers;
    private readonly IScreenCapture? _capture;
    private readonly ICaptureDmabufConstraints? _dmabuf;
    private readonly List<Session> _sessions = [];
    private readonly List<Frame> _waiting = [];
    private readonly List<CursorSession> _cursorSessions = [];

    public ImageCopyCaptureManager(
        WlServerDisplay display,
        ClientBufferRegistry buffers,
        IScreenCapture? capture,
        ICaptureDmabufConstraints? dmabuf = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(buffers);
        _buffers = buffers;
        _capture = capture;
        _dmabuf = dmabuf;
        _global = display.CreateGlobal(ExtImageCopyCaptureManagerV1.Interface, Version, OnBind);
        if (_capture is { } live)
        {
            live.AddDamageObserver(this);
        }
    }

    public event Action<CaptureSource, int>? SessionCountChanged;

    private void ReportSessionCount(in CaptureSource source)
    {
        if (SessionCountChanged is not { } changed)
        {
            return;
        }

        var count = 0;
        foreach (var session in _sessions)
        {
            if (session.Source.Equals(source))
            {
                count++;
            }
        }

        changed(source, count);
    }

    public void Dispose()
    {
        if (_capture is { } live)
        {
            live.RemoveDamageObserver(this);
        }

        _global.Dispose();
    }

    public void OnSourceDamaged(IOutput output, Box damage)
    {
        foreach (var session in _sessions)
        {
            if (session.Source.OutputTarget == output)
            {
                session.Damage.Add(damage);
            }
            else if (session.Source.Kind == CaptureSourceKind.Toplevel)
            {
                session.Damage.Add(new Box(0, 0, session.Width, session.Height));
            }
        }

        for (var i = _waiting.Count - 1; i >= 0; i--)
        {
            var frame = _waiting[i];
            if (frame.Session.Damage.HasDamage)
            {
                _waiting.RemoveAt(i);
                frame.Complete();
            }
        }
    }

    public void OnCursorChanged()
    {
        for (var i = _cursorSessions.Count - 1; i >= 0; i--)
        {
            _cursorSessions[i].Refresh();
        }
    }

    private void SendDmabufConstraints(ExtImageCopyCaptureSessionV1Resource resource, ReadOnlySpan<DrmFormat> formats)
    {
        if (_capture is null || _dmabuf is not { } constraints || !constraints.TryDevice(out var device))
        {
            return;
        }

        var offered = false;
        foreach (var format in formats)
        {
            var modifiers = constraints.Formats.ModifiersOf(format)
                .Where(m => m != DrmFormatSet.ModifierInvalid)
                .ToList();
            if (modifiers.Count == 0)
            {
                continue;
            }

            var encoded = new byte[modifiers.Count * sizeof(ulong)];
            for (var i = 0; i < modifiers.Count; i++)
            {
                BitConverter.TryWriteBytes(encoded.AsSpan(i * sizeof(ulong)), modifiers[i]);
            }

            resource.SendDmabufFormat((uint)format, encoded);
            offered = true;
        }

        if (offered)
        {
            resource.SendDmabufDevice(BitConverter.GetBytes(device));
        }
    }

    private bool Accepts(IBuffer target) =>
        !target.TryGetDmabuf(out var attributes) ||
        (_dmabuf is { } constraints && constraints.Formats.Contains(attributes.Format, attributes.Modifier));

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ExtImageCopyCaptureManagerV1Resource(client, version, id);
        manager.CreateSession += (_, e) =>
        {
            var resource = new ExtImageCopyCaptureSessionV1Resource(client, manager.Version, e.Session);
            if (((uint)e.Options & ~1u) != 0)
            {
                manager.PostError(ErrorInvalidOption, "invalid options bitfield");
                return;
            }

            var source = e.Source is { } sourceResource
                ? ImageCaptureSourceManager.FromResource(sourceResource.RawHandle)
                : default;
            var session = new Session(this, resource, source, ((uint)e.Options & 1u) != 0);
            if (session.Stopped)
            {
                resource.SendStopped();
                return;
            }

            _sessions.Add(session);
            ReportSessionCount(session.Source);
            resource.Destroyed += (_, _) =>
            {
                _sessions.Remove(session);
                ReportSessionCount(session.Source);
            };
            session.SendConstraints();
        };
        manager.CreatePointerCursorSession += (_, e) =>
        {
            var resource = new ExtImageCopyCaptureCursorSessionV1Resource(client, manager.Version, e.Session);
            var source = e.Source is { } sourceResource
                ? ImageCaptureSourceManager.FromResource(sourceResource.RawHandle)
                : default;
            var cursorSession = new CursorSession(this, resource, source.OutputTarget);
            _cursorSessions.Add(cursorSession);
            resource.Destroyed += (_, _) => _cursorSessions.Remove(cursorSession);
        };
    }

    internal sealed class DamageAccumulator
    {
        private Box _damage;

        public DamageAccumulator(Box initial)
        {
            _damage = initial;
            HasDamage = initial.Width > 0 && initial.Height > 0;
        }

        public bool HasDamage { get; private set; }

        public void Add(Box box)
        {
            if (box.Width <= 0 || box.Height <= 0)
            {
                return;
            }

            if (!HasDamage)
            {
                _damage = box;
                HasDamage = true;
                return;
            }

            var x1 = Math.Min(_damage.X, box.X);
            var y1 = Math.Min(_damage.Y, box.Y);
            var x2 = Math.Max(_damage.X + _damage.Width, box.X + box.Width);
            var y2 = Math.Max(_damage.Y + _damage.Height, box.Y + box.Height);
            _damage = new Box(x1, y1, x2 - x1, y2 - y1);
        }

        public Box Take()
        {
            HasDamage = false;
            return _damage;
        }
    }

    internal sealed class Session
    {
        private readonly ImageCopyCaptureManager _owner;
        private readonly ExtImageCopyCaptureSessionV1Resource _resource;
        private Frame? _activeFrame;

        public Session(
            ImageCopyCaptureManager owner,
            ExtImageCopyCaptureSessionV1Resource resource,
            in CaptureSource source,
            bool paintCursors)
        {
            _owner = owner;
            _resource = resource;
            Source = source;
            PaintCursors = paintCursors;

            if (owner._capture is { } capture && capture.Supports(source) && capture.TryDescribe(source, out var format))
            {
                (Width, Height) = (format.Width, format.Height);
            }

            Stopped = Width <= 0 || Height <= 0;
            Damage = new DamageAccumulator(new Box(0, 0, Width, Height));

            resource.CreateFrame += (_, e) =>
            {
                var frameResource = new ExtImageCopyCaptureFrameV1Resource(resource.Client, resource.Version, e.Frame);
                if (_activeFrame is not null)
                {
                    resource.PostError(ErrorDuplicateFrame, "previous frame not destroyed");
                    return;
                }

                var frame = new Frame(_owner, this, frameResource);
                _activeFrame = frame;
                frameResource.Destroyed += (_, _) =>
                {
                    _owner._waiting.Remove(frame);
                    if (_activeFrame == frame)
                    {
                        _activeFrame = null;
                    }
                };
            };
        }

        public CaptureSource Source { get; }

        public bool PaintCursors { get; }

        public int Width { get; }

        public int Height { get; }

        public bool Stopped { get; private set; }

        public DamageAccumulator Damage { get; }

        public WlOutput.Transform Transform => Source is { Kind: CaptureSourceKind.Output, OutputTarget: { } output }
            ? (WlOutput.Transform)output.Transform
            : WlOutput.Transform.Normal;

        public void SendConstraints()
        {
            _resource.SendBufferSize((uint)Width, (uint)Height);
            _resource.SendShmFormat(WlShm.Format.Xrgb8888);
            _resource.SendShmFormat(WlShm.Format.Argb8888);
            _owner.SendDmabufConstraints(_resource, [DrmFormat.Xrgb8888, DrmFormat.Argb8888]);
            _resource.SendDone();
        }

        public bool Render(IBuffer target)
        {
            if (_owner._capture is not { } capture)
            {
                return false;
            }

            var source = PaintCursors && Source is { Kind: CaptureSourceKind.Output, OutputTarget: { } output }
                ? CaptureSource.Output(output, overlayCursor: true)
                : Source;
            return capture.Capture(source, default, target);
        }

        public void Stop()
        {
            if (Stopped)
            {
                return;
            }

            Stopped = true;
            if (!_resource.IsDestroyed)
            {
                _resource.SendStopped();
            }
        }
    }

    internal sealed class Frame
    {
        private readonly ImageCopyCaptureManager _owner;
        private readonly ExtImageCopyCaptureFrameV1Resource _resource;
        private nint _bufferHandle;
        private bool _captured;

        public Frame(ImageCopyCaptureManager owner, Session session, ExtImageCopyCaptureFrameV1Resource resource)
        {
            _owner = owner;
            Session = session;
            _resource = resource;

            resource.AttachBuffer += (_, e) =>
            {
                if (_captured)
                {
                    resource.PostError(ErrorAlreadyCaptured, "attach_buffer after capture");
                    return;
                }

                _bufferHandle = e.BufferHandle;
            };
            resource.DamageBuffer += (_, e) =>
            {
                if (_captured)
                {
                    resource.PostError(ErrorAlreadyCaptured, "damage_buffer after capture");
                    return;
                }

                if (e.X < 0 || e.Y < 0 || e.Width <= 0 || e.Height <= 0)
                {
                    resource.PostError(ErrorInvalidBufferDamage, "invalid buffer damage");
                }
            };
            resource.Capture += (_, _) => OnCapture();
        }

        public Session Session { get; }

        public void Complete()
        {
            if (_resource.IsDestroyed)
            {
                return;
            }

            if (Session.Stopped)
            {
                SendFailed(ExtImageCopyCaptureFrameV1.FailureReason.Stopped);
                return;
            }

            var buffer = _owner._buffers.GetOrImport(_bufferHandle);
            if (buffer is null || buffer.Width != Session.Width || buffer.Height != Session.Height || !_owner.Accepts(buffer))
            {
                SendFailed(ExtImageCopyCaptureFrameV1.FailureReason.BufferConstraints);
                return;
            }

            var ok = Session.Render(buffer);
            if (!ok)
            {
                SendFailed(ExtImageCopyCaptureFrameV1.FailureReason.Unknown);
                return;
            }

            var damage = Session.Damage.Take();
            _resource.SendTransform(Session.Transform);
            _resource.SendDamage(damage.X, damage.Y, damage.Width, damage.Height);
            SendPresentationTime(_resource);
            _resource.SendReady();
        }

        private void OnCapture()
        {
            if (_captured)
            {
                _resource.PostError(ErrorAlreadyCaptured, "capture sent twice");
                return;
            }

            if (_bufferHandle == 0)
            {
                _resource.PostError(ErrorNoBuffer, "capture without attach_buffer");
                return;
            }

            _captured = true;
            if (Session.Stopped)
            {
                SendFailed(ExtImageCopyCaptureFrameV1.FailureReason.Stopped);
                return;
            }

            if (Session.Damage.HasDamage)
            {
                Complete();
            }
            else
            {
                _owner._waiting.Add(this);
            }
        }

        private void SendFailed(ExtImageCopyCaptureFrameV1.FailureReason reason)
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendFailed(reason);
            }
        }
    }

    private sealed class CursorSession
    {
        private readonly ImageCopyCaptureManager _owner;
        private readonly ExtImageCopyCaptureCursorSessionV1Resource _resource;
        private readonly IOutput? _output;
        private CursorCaptureSession? _capture;
        private bool _entered;

        public CursorSession(ImageCopyCaptureManager owner, ExtImageCopyCaptureCursorSessionV1Resource resource, IOutput? output)
        {
            _owner = owner;
            _resource = resource;
            _output = output;
            resource.GetCaptureSession += (_, e) =>
            {
                var sessionResource = new ExtImageCopyCaptureSessionV1Resource(resource.Client, resource.Version, e.Session);
                if (_capture is not null)
                {
                    resource.PostError(ErrorDuplicateSession, "get_capture_session sent twice");
                    return;
                }

                _capture = new CursorCaptureSession(_owner, sessionResource, _output);
                _capture.SendConstraints();
            };

            Refresh();
        }

        public void Refresh()
        {
            if (_resource.IsDestroyed)
            {
                return;
            }

            if (!TryCursor(out var cursor))
            {
                if (_entered)
                {
                    _resource.SendLeave();
                    _entered = false;
                }

                return;
            }

            if (!_entered)
            {
                _resource.SendEnter();
                _entered = true;
            }

            _resource.SendPosition(cursor.X, cursor.Y);
            _resource.SendHotspot(cursor.HotspotX, cursor.HotspotY);
            _capture?.Refresh();
        }

        private bool TryCursor(out CaptureCursorState cursor)
        {
            cursor = default;
            return _output is { } output
                && _owner._capture is { } capture
                && capture.TryCursorState(output, out cursor);
        }
    }

    private sealed class CursorCaptureSession
    {
        private readonly ImageCopyCaptureManager _owner;
        private readonly ExtImageCopyCaptureSessionV1Resource _resource;
        private readonly IOutput? _output;
        private readonly DamageAccumulator _damage;
        private readonly List<CursorFrame> _waiting = [];
        private (int Width, int Height)? _sent;

        private sealed class CursorFrame
        {
            public required ExtImageCopyCaptureFrameV1Resource Resource;
            public nint BufferHandle;
            public bool Captured;
        }

        public CursorCaptureSession(ImageCopyCaptureManager owner, ExtImageCopyCaptureSessionV1Resource resource, IOutput? output)
        {
            _owner = owner;
            _resource = resource;
            _output = output;
            _damage = new DamageAccumulator(TryDescribe(out var format) ? new Box(0, 0, format.Width, format.Height) : default);

            resource.CreateFrame += (_, e) =>
            {
                var frameResource = new ExtImageCopyCaptureFrameV1Resource(resource.Client, resource.Version, e.Frame);
                var frame = new CursorFrame { Resource = frameResource };
                frameResource.AttachBuffer += (_, ae) => frame.BufferHandle = ae.BufferHandle;
                frameResource.Capture += (_, _) => OnCapture(frame);
                frameResource.Destroyed += (_, _) => _waiting.RemoveAll(w => w == frame);
            };
        }

        public void SendConstraints()
        {
            if (_resource.IsDestroyed || !TryDescribe(out var format))
            {
                return;
            }

            _sent = (format.Width, format.Height);
            _resource.SendBufferSize((uint)format.Width, (uint)format.Height);
            _resource.SendShmFormat(WlShm.Format.Argb8888);
            _owner.SendDmabufConstraints(_resource, [DrmFormat.Argb8888]);
            _resource.SendDone();
        }

        public void Refresh()
        {
            if (TryDescribe(out var format))
            {
                if (_sent != (format.Width, format.Height))
                {
                    SendConstraints();
                }

                _damage.Add(new Box(0, 0, format.Width, format.Height));
            }

            for (var i = _waiting.Count - 1; i >= 0; i--)
            {
                var frame = _waiting[i];
                _waiting.RemoveAt(i);
                Complete(frame);
            }
        }

        private bool TryDescribe(out CaptureFormat format)
        {
            format = default;
            return _output is { } output
                && _owner._capture is { } capture
                && capture.TryDescribe(CaptureSource.Cursor(output), out format);
        }

        private void OnCapture(CursorFrame frame)
        {
            if (frame.Captured)
            {
                frame.Resource.PostError(ErrorAlreadyCaptured, "capture sent twice");
                return;
            }

            if (frame.BufferHandle == 0)
            {
                frame.Resource.PostError(ErrorNoBuffer, "capture without attach_buffer");
                return;
            }

            frame.Captured = true;
            if (_damage.HasDamage)
            {
                Complete(frame);
            }
            else
            {
                _waiting.Add(frame);
            }
        }

        private void Complete(CursorFrame frame)
        {
            if (frame.Resource.IsDestroyed)
            {
                return;
            }

            var target = _owner._buffers.GetOrImport(frame.BufferHandle);
            if (!TryDescribe(out var format) || target is null ||
                target.Width != format.Width || target.Height != format.Height || !_owner.Accepts(target))
            {
                frame.Resource.SendFailed(ExtImageCopyCaptureFrameV1.FailureReason.BufferConstraints);
                return;
            }

            var ok = _owner._capture!.Capture(CaptureSource.Cursor(_output!), default, target);
            if (!ok)
            {
                frame.Resource.SendFailed(ExtImageCopyCaptureFrameV1.FailureReason.Unknown);
                return;
            }

            var damage = _damage.Take();
            frame.Resource.SendTransform(WlOutput.Transform.Normal);
            frame.Resource.SendDamage(damage.X, damage.Y, damage.Width, damage.Height);
            SendPresentationTime(frame.Resource);
            frame.Resource.SendReady();
        }
    }

    private static void SendPresentationTime(ExtImageCopyCaptureFrameV1Resource resource)
    {
        var ticks = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = (ulong)(ticks / System.Diagnostics.Stopwatch.Frequency);
        var nanoseconds = (uint)((ticks % System.Diagnostics.Stopwatch.Frequency) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency);
        resource.SendPresentationTime((uint)(seconds >> 32), (uint)seconds, nanoseconds);
    }
}
