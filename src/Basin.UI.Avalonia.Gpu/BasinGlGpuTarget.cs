using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;
using Avalonia.Platform;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Pixman;
using static Basin.UI.Avalonia.AvaloniaGpuLog;
using MesaEglImage = Mesa.Egl.EglImage;

namespace Basin.UI.Avalonia;

internal sealed class BasinGlGpuTarget : IAvaloniaGpuTarget
{
    private const int MaxBuffers = 4;
    private const long IdleMillis = 5000;
    private const int StarvedTicks = 120;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly PixmanRegion32 _whole = new();
    private readonly List<IBuffer> _retired = [];
    private readonly List<Slot> _slots = [];
    private readonly GlDevice _device;
    private readonly IAllocator _allocator;
    private readonly ulong[] _modifiers;
    private Slot? _front;
    private Slot? _drawing;
    private int _width;
    private int _height;
    private double _scale = 1.0;
    private bool _produced;
    private bool _warnedStarved;
    private int _starved;
    private bool _disposed;

    internal BasinGlGpuTarget(GlDevice device, IAllocator allocator, ulong[] modifiers)
    {
        _device = device;
        _allocator = allocator;
        _modifiers = modifiers;
    }

    public UISurfaceSize Size => new(_width, _height, _scale);

    public bool Produced => _produced;

    public PixmanRegion32 WholeDamage => _whole;

    public bool Configure(int logicalWidth, int logicalHeight, double scale)
    {
        _thread.Assert();
        if (_disposed || logicalWidth <= 0 || logicalHeight <= 0 || scale <= 0)
        {
            return false;
        }

        scale = OutputScaling.Snap(scale);
        if (logicalWidth == _width && logicalHeight == _height && scale == _scale && _slots.Count > 0)
        {
            return true;
        }

        var physical = OutputScaling.ToPhysical(new Box(0, 0, logicalWidth, logicalHeight), scale);
        if (physical.IsEmpty)
        {
            return false;
        }

        var slot = Allocate(physical.Width, physical.Height);
        if (slot is null)
        {
            return false;
        }

        RetireAll();
        _slots.Add(slot);
        _produced = false;
        _starved = 0;
        _width = logicalWidth;
        _height = logicalHeight;
        _scale = scale;
        return true;
    }

    public bool TryAcquire(out UIFrame frame)
    {
        _thread.Assert();
        if (_disposed || !_produced || _front is null)
        {
            frame = default;
            return false;
        }

        frame = new UIFrame(_front.Buffer.Lock(), damage: null, RenderFences.DuplicateFence(_front.FenceFd));
        return true;
    }

    public void Trim(long nowMillis)
    {
        _thread.Assert();
        for (var i = _slots.Count - 1; i >= 0 && _slots.Count > 1; i--)
        {
            var slot = _slots[i];
            if (ReferenceEquals(slot, _front) || ReferenceEquals(slot, _drawing) || slot.Buffer.LockCount != 0)
            {
                slot.FreeSince = 0;
                continue;
            }

            if (slot.FreeSince == 0)
            {
                slot.FreeSince = nowMillis;
                continue;
            }

            if (nowMillis - slot.FreeSince < IdleMillis)
            {
                continue;
            }

            _slots.RemoveAt(i);
            RenderFences.CloseFence(slot.FenceFd);
            slot.FenceFd = -1;
            slot.Image.Dispose();
            Destroy(slot.Buffer);
        }
    }

    public IGlPlatformSurfaceRenderTarget CreateRenderTarget(IGlContext context, Action onFramePublished)
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (context is not EglContext egl)
        {
            throw new InvalidOperationException("The Avalonia GPU target renders through EGL only.");
        }

        return new SurfaceRenderTarget(this, egl, onFramePublished);
    }

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RetireAll();
        foreach (var buffer in _retired.ToArray())
        {
            if (!buffer.IsDestroyed)
            {
                Destroy(buffer);
            }
        }

        _retired.Clear();
        _whole.Dispose();
    }

    private Slot? Allocate(int width, int height)
    {
        var allocated = _allocator.Allocate(
            width, height, DrmFormat.Argb8888, _modifiers, BufferUse.Render | BufferUse.Scanout);
        if (allocated is null)
        {
            return null;
        }

        if (!allocated.TryGetDmabuf(out var attributes))
        {
            Destroy(allocated);
            return null;
        }

        var image = _device.ImportDmabufImage(attributes);
        if (image is null)
        {
            Destroy(allocated);
            return null;
        }

        return new Slot(allocated, image);
    }

    private bool CanDraw()
    {
        if (_disposed)
        {
            return false;
        }

        if (_slots.Count < MaxBuffers || FreeSlot() is not null)
        {
            _starved = 0;
            return true;
        }

        return ++_starved >= StarvedTicks;
    }

    private Slot? FreeSlot()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Buffer.LockCount == 0)
            {
                return _slots[i];
            }
        }

        return null;
    }

    private Slot? BeginSlot()
    {
        _thread.Assert();
        if (_disposed || _slots.Count == 0)
        {
            return null;
        }

        var free = FreeSlot();
        if (free is null && _slots.Count < MaxBuffers &&
            Allocate(_slots[0].Buffer.Width, _slots[0].Buffer.Height) is { } grown)
        {
            _slots.Add(grown);
            free = grown;
        }

        if (free is null)
        {
            free = _front ?? _slots[0];
            if (!_warnedStarved)
            {
                _warnedStarved = true;
                Log.Warn(
                    $"{_width}x{_height} chrome redraws into a buffer the compositor still holds; {_slots.Count} in flight");
            }
        }

        _starved = 0;
        free.FreeSince = 0;
        _drawing = free;
        return free;
    }

    private void AttachFence(Slot slot)
    {
        RenderFences.CloseFence(slot.FenceFd);
        slot.FenceFd = _device.ExportFence();
        if (slot.FenceFd < 0)
        {
            _device.Gl.Finish();
            return;
        }

        if (slot.Buffer.TryGetDmabuf(out var attributes))
        {
            RenderFences.PublishFenceTo(attributes, forWrite: true, slot.FenceFd);
        }
    }

    private void WaitForReaders(Slot slot)
    {
        if (!slot.Buffer.TryGetDmabuf(out var attributes))
        {
            return;
        }

        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            var fence = RenderFences.ExportDmabufSyncFile(attributes.Fds[plane], forWrite: true);
            if (fence < 0)
            {
                continue;
            }

            _device.WaitFence(fence);
            RenderFences.CloseFence(fence);
        }
    }

    private void EndSlot()
    {
        if (_drawing is { } drawn)
        {
            _front = drawn;
            _drawing = null;
        }

        _produced = true;
    }

    private void RetireAll()
    {
        _front = null;
        _drawing = null;
        for (var i = 0; i < _slots.Count; i++)
        {
            Retire(_slots[i]);
        }

        _slots.Clear();
    }

    private void Retire(Slot slot)
    {
        RenderFences.CloseFence(slot.FenceFd);
        slot.FenceFd = -1;
        slot.Image.Dispose();
        var buffer = slot.Buffer;
        if (buffer.LockCount == 0)
        {
            Destroy(buffer);
            return;
        }

        _retired.Add(buffer);
        buffer.Released += () =>
        {
            if (_retired.Remove(buffer) && !buffer.IsDestroyed)
            {
                Destroy(buffer);
            }
        };
    }

    private static void Destroy(IBuffer buffer)
    {
        if (buffer is BufferBase concrete)
        {
            concrete.Destroy();
        }
    }

    private sealed class Slot
    {
        internal Slot(IBuffer buffer, MesaEglImage image)
        {
            Buffer = buffer;
            Image = image;
        }

        internal IBuffer Buffer { get; }

        internal MesaEglImage Image { get; }

        internal long FreeSince { get; set; }

        internal int FenceFd { get; set; } = -1;
    }

    private sealed class SurfaceRenderTarget : EglPlatformImageSurfaceRenderTargetBase
    {
        private readonly BasinGlGpuTarget _owner;
        private readonly Action _onPublished;

        public SurfaceRenderTarget(BasinGlGpuTarget owner, EglContext context, Action onFramePublished)
            : base(context)
        {
            _owner = owner;
            _onPublished = () =>
            {
                owner.EndSlot();
                onFramePublished();
            };
        }

        public override PlatformRenderTargetState State
        {
            get
            {
                var state = base.State;
                return state.IsReady && !state.IsCorrupted && !_owner.CanDraw()
                    ? PlatformRenderTargetState.NotReadyTryLater
                    : state;
            }
        }

        public override IGlPlatformSurfaceRenderingSession BeginDrawCore(
            IRenderTarget.RenderTargetSceneInfo sceneInfo)
        {
            if (_owner._disposed || _owner.BeginSlot() is not { } slot)
            {
                throw new RenderTargetCorruptedException();
            }

            var session = BeginDraw(
                slot.Image.Handle,
                new PixelSize(slot.Buffer.Width, slot.Buffer.Height),
                _owner._scale,
                _onPublished);
            _owner.WaitForReaders(slot);
            return new FencedSession(_owner, slot, session);
        }
    }

    private sealed class FencedSession : IGlPlatformSurfaceRenderingSession
    {
        private readonly BasinGlGpuTarget _owner;
        private readonly Slot _slot;
        private readonly IGlPlatformSurfaceRenderingSession _session;

        internal FencedSession(BasinGlGpuTarget owner, Slot slot, IGlPlatformSurfaceRenderingSession session)
        {
            _owner = owner;
            _slot = slot;
            _session = session;
        }

        public IGlContext Context => _session.Context;

        public PixelSize Size => _session.Size;

        public double Scaling => _session.Scaling;

        public bool IsYFlipped => _session.IsYFlipped;

        public void Dispose()
        {
            _owner.AttachFence(_slot);
            _session.Dispose();
        }
    }
}
