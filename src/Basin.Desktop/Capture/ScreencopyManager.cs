using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ScreencopyManager : ICaptureDamageObserver, IDisposable
{
    public const int Version = 3;

    private readonly WlGlobal _global;
    private readonly OutputLayout _layout;
    private readonly ClientBufferRegistry _buffers;
    private readonly IScreenCapture? _capture;
    private readonly ICaptureDmabufConstraints? _dmabuf;
    private readonly List<Frame> _waiting = [];
    private readonly Dictionary<(ZwlrScreencopyManagerV1Resource Manager, IOutput Output), Accumulator> _accumulated = [];

    private sealed class Accumulator
    {
        private Box _damage;
        private bool _hasDamage;

        public Accumulator(Box initial)
        {
            _damage = initial;
            _hasDamage = initial.Width > 0 && initial.Height > 0;
        }

        public bool HasDamage => _hasDamage;

        public void Add(Box box)
        {
            if (box.Width <= 0 || box.Height <= 0)
            {
                return;
            }

            if (!_hasDamage)
            {
                _damage = box;
                _hasDamage = true;
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
            _hasDamage = false;
            return _damage;
        }
    }

    public ScreencopyManager(
        WlServerDisplay display,
        OutputLayout layout,
        ClientBufferRegistry buffers,
        IScreenCapture? capture,
        ICaptureDmabufConstraints? dmabuf = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(buffers);
        _layout = layout;
        _buffers = buffers;
        _capture = capture;
        _dmabuf = dmabuf;
        _global = display.CreateGlobal(ZwlrScreencopyManagerV1.Interface, Version, OnBind);
        if (_capture is { } live)
        {
            live.AddDamageObserver(this);
        }
    }

    public void Dispose()
    {
        if (_capture is { } live)
        {
            live.RemoveDamageObserver(this);
        }

        _global.Dispose();
    }

    public void OnSourceDamaged(IOutput output, Box damage) => NotifyOutputDamaged(output, damage);

    public void OnCursorChanged()
    {
    }

    public void NotifyOutputDamaged(IOutput output, Box damage)
    {
        foreach (var (key, accumulator) in _accumulated)
        {
            if (key.Output == output)
            {
                accumulator.Add(damage);
            }
        }

        for (var i = _waiting.Count - 1; i >= 0; i--)
        {
            var frame = _waiting[i];
            if (frame.Output == output)
            {
                _waiting.RemoveAt(i);
                frame.CompleteWithDamage(frame.TakeAccumulatedDamage());
            }
        }
    }

    private const DrmFormat CaptureFormat = DrmFormat.Xrgb8888;

    private bool OffersDmabuf =>
        _capture is not null &&
        _dmabuf is { } constraints &&
        constraints.TryDevice(out _) &&
        constraints.Formats.Contains(CaptureFormat);

    private bool Accepts(IBuffer target) =>
        !target.TryGetDmabuf(out var attributes) ||
        (_dmabuf is { } constraints && constraints.Formats.Contains(attributes.Format, attributes.Modifier));

    private Accumulator AccumulatorFor(ZwlrScreencopyManagerV1Resource manager, IOutput output)
    {
        var key = (manager, output);
        if (!_accumulated.TryGetValue(key, out var accumulator))
        {
            var mode = output.CurrentMode;
            accumulator = new Accumulator(new Box(0, 0, mode.Width, mode.Height));
            _accumulated[key] = accumulator;
        }

        return accumulator;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwlrScreencopyManagerV1Resource(client, version, id);
        manager.Destroyed += (_, _) =>
        {
            foreach (var key in _accumulated.Keys.Where(k => k.Manager == manager).ToList())
            {
                _accumulated.Remove(key);
            }
        };
        manager.CaptureOutput += (_, e) =>
        {
            var output = OutputGlobal.FromResource(e.Output)?.Output;
            var frame = new ZwlrScreencopyFrameV1Resource(client, manager.Version, e.Frame);
            if (output is null)
            {
                frame.SendFailed();
                return;
            }

            var mode = output.CurrentMode;
            _ = new Frame(
                this,
                frame,
                output,
                new Box(0, 0, mode.Width, mode.Height),
                AccumulatorFor(manager, output),
                e.OverlayCursor != 0);
        };
        manager.CaptureOutputRegion += (_, e) =>
        {
            var output = OutputGlobal.FromResource(e.Output)?.Output;
            var frame = new ZwlrScreencopyFrameV1Resource(client, manager.Version, e.Frame);
            if (output is null)
            {
                frame.SendFailed();
                return;
            }

            var mode = output.CurrentMode;
            var physical = OutputScaling.ToPhysical(new Box(e.X, e.Y, e.Width, e.Height), output.Scale);
            var x = Math.Clamp(physical.X, 0, mode.Width);
            var y = Math.Clamp(physical.Y, 0, mode.Height);
            var region = new Box(
                x,
                y,
                Math.Clamp(physical.Width, 0, mode.Width - x),
                Math.Clamp(physical.Height, 0, mode.Height - y));
            _ = new Frame(this, frame, output, region, AccumulatorFor(manager, output), e.OverlayCursor != 0);
        };
    }

    private sealed class Frame
    {
        private readonly ScreencopyManager _owner;
        private readonly ZwlrScreencopyFrameV1Resource _resource;
        private readonly Box _region;
        private readonly Accumulator _accumulator;
        private readonly bool _overlayCursor;
        private nint _bufferHandle;

        public Frame(
            ScreencopyManager owner,
            ZwlrScreencopyFrameV1Resource resource,
            IOutput output,
            Box region,
            Accumulator accumulator,
            bool overlayCursor)
        {
            _owner = owner;
            _resource = resource;
            Output = output;
            _region = region;
            _accumulator = accumulator;
            _overlayCursor = overlayCursor;

            resource.SendBuffer(WlShm.Format.Xrgb8888, (uint)region.Width, (uint)region.Height, (uint)(region.Width * 4));
            if (resource.Version >= 3)
            {
                if (owner.OffersDmabuf)
                {
                    resource.SendLinuxDmabuf((uint)CaptureFormat, (uint)region.Width, (uint)region.Height);
                }

                resource.SendBufferDone();
            }

            resource.Copy += (_, e) => OnCopy(e.BufferHandle, withDamage: false);
            resource.CopyWithDamage += (_, e) => OnCopy(e.BufferHandle, withDamage: true);
            resource.Destroyed += (_, _) => _owner._waiting.Remove(this);
        }

        public IOutput Output { get; }

        private void OnCopy(nint bufferHandle, bool withDamage)
        {
            _bufferHandle = bufferHandle;
            if (!withDamage)
            {
                Complete(damage: null);
            }
            else if (_accumulator.HasDamage)
            {
                Complete(_accumulator.Take());
            }
            else
            {
                _owner._waiting.Add(this);
            }
        }

        public void CompleteWithDamage(Box damage) => Complete(damage);

        public Box TakeAccumulatedDamage() => _accumulator.Take();

        private void Complete(Box? damage)
        {
            if (_resource.IsDestroyed)
            {
                return;
            }

            var buffer = _owner._buffers.GetOrImport(_bufferHandle);
            if (buffer is null || buffer.Width != _region.Width || buffer.Height != _region.Height || !_owner.Accepts(buffer))
            {
                _resource.SendFailed();
                return;
            }

            var ok = CopyInto(buffer);
            if (!ok)
            {
                _resource.SendFailed();
                return;
            }

            _resource.SendFlags(0);
            if (damage is { } box && _resource.Version >= 2)
            {
                _resource.SendDamage((uint)box.X, (uint)box.Y, (uint)Math.Max(0, box.Width), (uint)Math.Max(0, box.Height));
            }

            var ticks = System.Diagnostics.Stopwatch.GetTimestamp();
            var seconds = (ulong)(ticks / System.Diagnostics.Stopwatch.Frequency);
            var nanoseconds = (uint)((ticks % System.Diagnostics.Stopwatch.Frequency) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency);
            _resource.SendReady((uint)(seconds >> 32), (uint)seconds, nanoseconds);
        }

        private bool CopyInto(IBuffer target)
        {
            if (_owner._capture is not { } capture)
            {
                return false;
            }

            var source = CaptureSource.Output(Output, _overlayCursor);
            return capture.Supports(source) && capture.Capture(source, _region, target);
        }
    }
}
