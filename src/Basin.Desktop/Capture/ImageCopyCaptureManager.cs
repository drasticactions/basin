using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Diagnostics;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>ext_image_copy_capture_manager_v1</c> global. It copies outputs, toplevels and the cursor into client buffers through <see cref="IScreenCapture"/>.
/// </summary>
public sealed class ImageCopyCaptureManager : ICaptureDamageObserver, IDisposable, IExtImageCopyCaptureManagerV1Handler
{
    private static readonly BasinLogger Log = BasinLog.For("capture");

    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCopyCaptureManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="buffers">The registry that resolves client buffers.</param>
    /// <param name="capture">The capture implementation. With <see langword="null"/>, every capture fails with the protocol's own failure event.</param>
    /// <param name="dmabuf">The dmabuf device and formats to offer, or <see langword="null"/> to offer shared memory only.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> or <paramref name="buffers"/> is <see langword="null"/>.</exception>
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

        if (_dmabuf is { } constraints)
        {
            constraints.Changed += ResendConstraints;
        }
    }

    /// <summary>
    /// Occurs when a capture session starts or stops. The arguments are the source and its number of live sessions after the change.
    /// </summary>
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

    /// <summary>
    /// Stops the damage observation and removes the global.
    /// </summary>
    public void Dispose()
    {
        if (_capture is { } live)
        {
            live.RemoveDamageObserver(this);
        }

        if (_dmabuf is { } constraints)
        {
            constraints.Changed -= ResendConstraints;
        }

        _global.Dispose();
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void OnCursorChanged()
    {
        for (var i = _cursorSessions.Count - 1; i >= 0; i--)
        {
            _cursorSessions[i].Refresh();
        }
    }

    /// <summary>
    /// Sends the buffer constraints again to every live session, for example after the render device changed.
    /// </summary>
    public void ResendConstraints()
    {
        foreach (var session in _sessions)
        {
            if (!session.Stopped && !session.Resource.IsDestroyed)
            {
                session.SendConstraints();
            }
        }

        foreach (var cursorSession in _cursorSessions)
        {
            cursorSession.ResendConstraints();
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

        if (WrapsYuv)
        {
            resource.SendDmabufFormat((uint)DrmFormat.Nv12, BitConverter.GetBytes(DrmFormatSet.ModifierLinear));
            offered = true;
        }

        if (offered)
        {
            resource.SendDmabufDevice(BitConverter.GetBytes(device));
        }
    }

    private bool WrapsYuv =>
        _capture is { CanCaptureYuv: true } && _dmabuf is { } constraints && constraints.Wraps(DrmFormat.Nv12, DrmFormatSet.ModifierLinear);

    private bool Accepts(IBuffer target) =>
        !target.TryGetDmabuf(out var attributes) ||
        (attributes.Format == DrmFormat.Nv12 && attributes.Modifier == DrmFormatSet.ModifierLinear && WrapsYuv) ||
        (_dmabuf is { } constraints && constraints.Formats.Contains(attributes.Format, attributes.Modifier));

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ExtImageCopyCaptureManagerV1Resource(client, version, id, this);

    void IExtImageCopyCaptureManagerV1Handler.CreateSession(ExtImageCopyCaptureManagerV1Resource resource, in ExtImageCopyCaptureManagerV1Resource.CreateSessionEventArgs args)
    {
        if (((uint)args.Options & ~1u) != 0)
        {
            _ = new ExtImageCopyCaptureSessionV1Resource(resource.Client, resource.Version, args.Session, IgnoredSession.Instance);
            resource.PostError(ErrorInvalidOption, "The options bitfield is not valid.");
            return;
        }

        var source = args.Source is { } sourceResource
            ? ImageCaptureSourceManager.FromResource(sourceResource.RawHandle)
            : default;
        var session = new Session(this, resource.Client, resource.Version, args.Session, source, ((uint)args.Options & 1u) != 0);
        if (session.Stopped)
        {
            session.Resource.SendStopped();
            return;
        }

        _sessions.Add(session);
        ReportSessionCount(session.Source);
        session.Resource.Destroyed += (_, _) =>
        {
            _sessions.Remove(session);
            ReportSessionCount(session.Source);
        };
        session.SendConstraints();
    }

    void IExtImageCopyCaptureManagerV1Handler.CreatePointerCursorSession(ExtImageCopyCaptureManagerV1Resource resource, in ExtImageCopyCaptureManagerV1Resource.CreatePointerCursorSessionEventArgs args)
    {
        var source = args.Source is { } sourceResource
            ? ImageCaptureSourceManager.FromResource(sourceResource.RawHandle)
            : default;
        var cursorSession = new CursorSession(this, resource.Client, resource.Version, args.Session, source.OutputTarget);
        _cursorSessions.Add(cursorSession);
        cursorSession.Resource.Destroyed += (_, _) => _cursorSessions.Remove(cursorSession);
    }

    void IExtImageCopyCaptureManagerV1Handler.Destroy(ExtImageCopyCaptureManagerV1Resource resource)
    {
    }

    private sealed class IgnoredSession : IExtImageCopyCaptureSessionV1Handler
    {
        public static readonly IgnoredSession Instance = new();

        void IExtImageCopyCaptureSessionV1Handler.CreateFrame(ExtImageCopyCaptureSessionV1Resource resource, in ExtImageCopyCaptureSessionV1Resource.CreateFrameEventArgs args)
        {
        }

        void IExtImageCopyCaptureSessionV1Handler.Destroy(ExtImageCopyCaptureSessionV1Resource resource)
        {
        }
    }

    private sealed class IgnoredFrame : IExtImageCopyCaptureFrameV1Handler
    {
        public static readonly IgnoredFrame Instance = new();

        void IExtImageCopyCaptureFrameV1Handler.AttachBuffer(ExtImageCopyCaptureFrameV1Resource resource, in ExtImageCopyCaptureFrameV1Resource.AttachBufferEventArgs args)
        {
        }

        void IExtImageCopyCaptureFrameV1Handler.DamageBuffer(ExtImageCopyCaptureFrameV1Resource resource, in ExtImageCopyCaptureFrameV1Resource.DamageBufferEventArgs args)
        {
        }

        void IExtImageCopyCaptureFrameV1Handler.Capture(ExtImageCopyCaptureFrameV1Resource resource)
        {
        }

        void IExtImageCopyCaptureFrameV1Handler.Destroy(ExtImageCopyCaptureFrameV1Resource resource)
        {
        }
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

    internal sealed class Session : IExtImageCopyCaptureSessionV1Handler
    {
        private readonly ImageCopyCaptureManager _owner;
        private readonly ExtImageCopyCaptureSessionV1Resource _resource;
        private Frame? _activeFrame;

        public Session(
            ImageCopyCaptureManager owner,
            WlClient client,
            uint version,
            uint id,
            in CaptureSource source,
            bool paintCursors)
        {
            _owner = owner;
            _resource = new ExtImageCopyCaptureSessionV1Resource(client, version, id, this);
            Source = source;
            PaintCursors = paintCursors;

            if (owner._capture is { } capture && capture.Supports(source) && capture.TryDescribe(source, out var format))
            {
                (Width, Height) = (format.Width, format.Height);
            }

            Stopped = Width <= 0 || Height <= 0;
            Damage = new DamageAccumulator(new Box(0, 0, Width, Height));
        }

        public ExtImageCopyCaptureSessionV1Resource Resource => _resource;

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

        void IExtImageCopyCaptureSessionV1Handler.CreateFrame(ExtImageCopyCaptureSessionV1Resource resource, in ExtImageCopyCaptureSessionV1Resource.CreateFrameEventArgs args)
        {
            if (_activeFrame is not null)
            {
                _ = new ExtImageCopyCaptureFrameV1Resource(resource.Client, resource.Version, args.Frame, IgnoredFrame.Instance);
                resource.PostError(ErrorDuplicateFrame, "The previous frame is not destroyed. Destroy it before the next frame.");
                return;
            }

            var frame = new Frame(_owner, this, resource.Client, resource.Version, args.Frame);
            _activeFrame = frame;
            frame.Resource.Destroyed += (_, _) =>
            {
                _owner._waiting.Remove(frame);
                if (_activeFrame == frame)
                {
                    _activeFrame = null;
                }
            };
        }

        void IExtImageCopyCaptureSessionV1Handler.Destroy(ExtImageCopyCaptureSessionV1Resource resource)
        {
        }
    }

    internal sealed class Frame : IExtImageCopyCaptureFrameV1Handler
    {
        private readonly ImageCopyCaptureManager _owner;
        private readonly ExtImageCopyCaptureFrameV1Resource _resource;
        private nint _bufferHandle;
        private bool _captured;

        public Frame(ImageCopyCaptureManager owner, Session session, WlClient client, uint version, uint id)
        {
            _owner = owner;
            Session = session;
            _resource = new ExtImageCopyCaptureFrameV1Resource(client, version, id, this);
        }

        public Session Session { get; }

        public ExtImageCopyCaptureFrameV1Resource Resource => _resource;

        void IExtImageCopyCaptureFrameV1Handler.AttachBuffer(ExtImageCopyCaptureFrameV1Resource resource, in ExtImageCopyCaptureFrameV1Resource.AttachBufferEventArgs args)
        {
            if (_captured)
            {
                resource.PostError(ErrorAlreadyCaptured, "attach_buffer came after capture. Attach the buffer before capture.");
                return;
            }

            _bufferHandle = args.BufferHandle;
        }

        void IExtImageCopyCaptureFrameV1Handler.DamageBuffer(ExtImageCopyCaptureFrameV1Resource resource, in ExtImageCopyCaptureFrameV1Resource.DamageBufferEventArgs args)
        {
            if (_captured)
            {
                resource.PostError(ErrorAlreadyCaptured, "damage_buffer came after capture. Damage the buffer before capture.");
                return;
            }

            if (args.X < 0 || args.Y < 0 || args.Width <= 0 || args.Height <= 0)
            {
                resource.PostError(ErrorInvalidBufferDamage, "The buffer damage is not valid.");
            }
        }

        void IExtImageCopyCaptureFrameV1Handler.Capture(ExtImageCopyCaptureFrameV1Resource resource) => OnCapture();

        void IExtImageCopyCaptureFrameV1Handler.Destroy(ExtImageCopyCaptureFrameV1Resource resource)
        {
        }

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

            bool ok;
            try
            {
                ok = Session.Render(buffer);
            }
            catch (InvalidOperationException e)
            {
                Log.Debug(e, $"image-copy: frame refused the client buffer");
                SendFailed(ExtImageCopyCaptureFrameV1.FailureReason.BufferConstraints);
                return;
            }

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
                _resource.PostError(ErrorAlreadyCaptured, "capture was sent two times. Send capture one time for each frame.");
                return;
            }

            if (_bufferHandle == 0)
            {
                _resource.PostError(ErrorNoBuffer, "capture came before attach_buffer. Attach a buffer before capture.");
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

    private sealed class CursorSession : IExtImageCopyCaptureCursorSessionV1Handler
    {
        private readonly ImageCopyCaptureManager _owner;
        private readonly ExtImageCopyCaptureCursorSessionV1Resource _resource;
        private readonly IOutput? _output;
        private CursorCaptureSession? _capture;
        private bool _entered;

        public CursorSession(ImageCopyCaptureManager owner, WlClient client, uint version, uint id, IOutput? output)
        {
            _owner = owner;
            _resource = new ExtImageCopyCaptureCursorSessionV1Resource(client, version, id, this);
            _output = output;
            Refresh();
        }

        public ExtImageCopyCaptureCursorSessionV1Resource Resource => _resource;

        public void ResendConstraints() => _capture?.SendConstraints();

        void IExtImageCopyCaptureCursorSessionV1Handler.GetCaptureSession(ExtImageCopyCaptureCursorSessionV1Resource resource, in ExtImageCopyCaptureCursorSessionV1Resource.GetCaptureSessionEventArgs args)
        {
            if (_capture is not null)
            {
                _ = new ExtImageCopyCaptureSessionV1Resource(resource.Client, resource.Version, args.Session, IgnoredSession.Instance);
                resource.PostError(ErrorDuplicateSession, "get_capture_session was sent two times. Send it one time for each frame.");
                return;
            }

            _capture = new CursorCaptureSession(_owner, resource.Client, resource.Version, args.Session, _output);
            _capture.SendConstraints();
        }

        void IExtImageCopyCaptureCursorSessionV1Handler.Destroy(ExtImageCopyCaptureCursorSessionV1Resource resource)
        {
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

    private sealed class CursorCaptureSession : IExtImageCopyCaptureSessionV1Handler
    {
        private readonly ImageCopyCaptureManager _owner;
        private readonly ExtImageCopyCaptureSessionV1Resource _resource;
        private readonly IOutput? _output;
        private readonly DamageAccumulator _damage;
        private readonly List<CursorFrame> _waiting = [];
        private (int Width, int Height)? _sent;

        private sealed class CursorFrame : IExtImageCopyCaptureFrameV1Handler
        {
            private readonly CursorCaptureSession _session;

            public CursorFrame(CursorCaptureSession session, WlClient client, uint version, uint id)
            {
                _session = session;
                Resource = new ExtImageCopyCaptureFrameV1Resource(client, version, id, this);
            }

            public ExtImageCopyCaptureFrameV1Resource Resource { get; }

            public nint BufferHandle { get; private set; }

            public bool Captured { get; set; }

            void IExtImageCopyCaptureFrameV1Handler.AttachBuffer(ExtImageCopyCaptureFrameV1Resource resource, in ExtImageCopyCaptureFrameV1Resource.AttachBufferEventArgs args) =>
                BufferHandle = args.BufferHandle;

            void IExtImageCopyCaptureFrameV1Handler.DamageBuffer(ExtImageCopyCaptureFrameV1Resource resource, in ExtImageCopyCaptureFrameV1Resource.DamageBufferEventArgs args)
            {
            }

            void IExtImageCopyCaptureFrameV1Handler.Capture(ExtImageCopyCaptureFrameV1Resource resource) => _session.OnCapture(this);

            void IExtImageCopyCaptureFrameV1Handler.Destroy(ExtImageCopyCaptureFrameV1Resource resource)
            {
            }
        }

        public CursorCaptureSession(ImageCopyCaptureManager owner, WlClient client, uint version, uint id, IOutput? output)
        {
            _owner = owner;
            _resource = new ExtImageCopyCaptureSessionV1Resource(client, version, id, this);
            _output = output;
            _damage = new DamageAccumulator(TryDescribe(out var format) ? new Box(0, 0, format.Width, format.Height) : default);
        }

        void IExtImageCopyCaptureSessionV1Handler.CreateFrame(ExtImageCopyCaptureSessionV1Resource resource, in ExtImageCopyCaptureSessionV1Resource.CreateFrameEventArgs args)
        {
            var frame = new CursorFrame(this, resource.Client, resource.Version, args.Frame);
            frame.Resource.Destroyed += (_, _) => _waiting.RemoveAll(w => w == frame);
        }

        void IExtImageCopyCaptureSessionV1Handler.Destroy(ExtImageCopyCaptureSessionV1Resource resource)
        {
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
                frame.Resource.PostError(ErrorAlreadyCaptured, "capture was sent two times. Send capture one time for each frame.");
                return;
            }

            if (frame.BufferHandle == 0)
            {
                frame.Resource.PostError(ErrorNoBuffer, "capture came before attach_buffer. Attach a buffer before capture.");
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
