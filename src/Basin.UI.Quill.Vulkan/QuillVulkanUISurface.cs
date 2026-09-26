using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Pixman;
using Prowl.Quill;
using Silk.NET.Vulkan;

namespace Basin.UI.Quill;

public sealed class QuillVulkanUISurface : IQuillUISurface
{
    private const int MaxFrames = 3;

    private sealed class Target
    {
        public required VulkanDeviceImage Image;
        public required Framebuffer Framebuffer;
        public ulong LastUsed;
    }

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly PixmanRegion32 _wholeDamage = new();
    private readonly UISurfaceObservers _observers = new();
    private const int MaxSpares = 2;

    private readonly List<IBuffer> _retired = [];
    private readonly List<IBuffer> _spares = [];
    private readonly HashSet<IBuffer> _watched = [];
    private readonly Dictionary<IBuffer, Target> _wraps = [];
    private readonly List<Target> _dropped = [];
    private readonly List<QuillVulkanFrame> _frames = [];
    private readonly VulkanDevice _device;
    private readonly IAllocator _allocator;
    private readonly QuillVulkanPipeline _pipeline;
    private readonly QuillVulkanTextures _textures;
    private readonly FontAtlasSettings _atlas;
    private readonly QuillVulkanUIHost? _host;
    private readonly QuillVulkanCanvasRenderer _renderer;
    private Canvas? _canvas;
    private QuillVulkanFrame? _frame;
    private CommandBuffer _commands;

    private IBuffer? _target;
    private IBuffer? _front;
    private ulong _lastPoint;
    private int _width;
    private int _height;
    private double _scale;
    private bool _drawing;
    private bool _canvasFrame;
    private bool _produced;
    private bool _disposed;

    internal QuillVulkanUISurface(
        VulkanDevice device,
        IAllocator allocator,
        QuillVulkanPipeline pipeline,
        QuillVulkanTextures textures,
        FontAtlasSettings atlas,
        QuillVulkanUIHost? host)
    {
        _device = device;
        _allocator = allocator;
        _pipeline = pipeline;
        _textures = textures;
        _atlas = atlas;
        _host = host;
        _renderer = new QuillVulkanCanvasRenderer(device, pipeline, textures);
    }

    public Canvas Canvas
    {
        get
        {
            _thread.Assert();
            return _canvas ??= new Canvas(_renderer, _atlas);
        }
    }

    public ICanvasRenderer Renderer => _renderer;

    public FontAtlasSettings Atlas => _atlas;

    public UISurfaceSize Size
    {
        get
        {
            _thread.Assert();
            return new UISurfaceSize(_width, _height, _scale);
        }
    }

    public bool AcceptsInput => true;

    internal int FrameSlots => _frames.Count;

    public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

    public bool Configure(int logicalWidth, int logicalHeight, double scale)
    {
        _thread.Assert();
        if (_disposed || _drawing || logicalWidth <= 0 || logicalHeight <= 0 || scale <= 0)
        {
            return false;
        }

        scale = OutputScaling.Snap(scale);
        var sizeUnchanged = logicalWidth == _width && logicalHeight == _height && scale == _scale;
        if (sizeUnchanged && _target is not null && !ReferenceEquals(_target, _front))
        {
            return true;
        }

        var physical = OutputScaling.ToPhysical(new Box(0, 0, logicalWidth, logicalHeight), scale);
        if (physical.IsEmpty)
        {
            return false;
        }

        if (!sizeUnchanged)
        {
            DropSpares();
        }

        var allocated = (sizeUnchanged ? TakeSpare() : null) ?? _allocator.Allocate(
            physical.Width, physical.Height, DrmFormat.Argb8888, Modifiers, BufferUse.Render | BufferUse.Scanout);
        if (allocated is null)
        {
            return false;
        }

        if (_target is not null && !ReferenceEquals(_target, _front))
        {
            Retire(_target);
        }

        _target = allocated;
        _produced = false;
        (_width, _height, _scale) = (logicalWidth, logicalHeight, scale);
        return true;
    }

    public void BeginTarget()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_drawing)
        {
            throw new InvalidOperationException("BeginDraw without EndDraw.");
        }

        var target = _target ?? throw new InvalidOperationException("BeginDraw before Configure.");
        var wrap = WrapOf(target);
        var frame = AcquireFrame();
        WaitForReaders(target);
        var commands = _device.BeginCommands();
        wrap.Image.RecordForeignAcquire(commands);
        _renderer.Open(commands, frame, wrap.Framebuffer, target.Width, target.Height);
        _frame = frame;
        _commands = commands;
        _drawing = true;
        _canvasFrame = false;
        _textures.OpenFrame();
    }

    public Canvas BeginDraw()
    {
        BeginTarget();
        var canvas = Canvas;
        _canvasFrame = true;
        canvas.BeginFrame(_width, _height, (float)_scale);
        return canvas;
    }

    public void EndTarget()
    {
        _thread.Assert();
        if (!_drawing)
        {
            throw new InvalidOperationException("EndDraw without BeginDraw.");
        }

        var target = _target!;
        var wrap = WrapOf(target);
        var frame = _frame!;
        var commands = _commands;
        ulong point;
        bool fenced;
        try
        {
            if (_renderer.Passes == 0)
            {
                _renderer.RecordClear();
            }
        }
        finally
        {
            _renderer.Close();
            _frame = null;
            _commands = default;
        }

        if (target.TryGetDmabuf(out var attributes))
        {
            fenced = _device.PublishWriteFence(commands, attributes, wrap.Image, out point);
        }
        else
        {
            wrap.Image.RecordForeignRelease(commands);
            point = _device.Submit(commands);
            fenced = false;
        }

        if (!fenced)
        {
            _device.WaitFor(point);
        }

        frame.Point = point;
        wrap.LastUsed = point;
        _lastPoint = point;
        _textures.CloseFrame();
        _textures.MarkSubmitted(point);
        _host?.ReportSync(fenced);
        ReleaseDropped();

        _drawing = false;
        _canvasFrame = false;
        _produced = true;
        if (ReferenceEquals(_target, _front))
        {
            _observers.Damaged(this, _wholeDamage);
        }
    }

    public void EndDraw()
    {
        _thread.Assert();
        if (!_drawing || !_canvasFrame)
        {
            throw new InvalidOperationException("EndDraw without BeginDraw.");
        }

        try
        {
            _canvas!.Render();
        }
        catch
        {
            _renderer.Close();
            throw;
        }

        EndTarget();
    }

    public bool TryAcquire(out UIFrame frame)
    {
        _thread.Assert();
        if (_disposed || !_produced || _target is null)
        {
            frame = default;
            return false;
        }

        if (_front is not null && !ReferenceEquals(_front, _target))
        {
            Retire(_front);
        }

        _front = _target;
        frame = new UIFrame(_front.Lock(), damage: null);
        return true;
    }

    public bool AcceptsInputAt(double x, double y)
    {
        _thread.Assert();
        return !_disposed && x >= 0 && y >= 0 && x < _width && y < _height;
    }

    public string? CursorAt(double x, double y) => null;

    public void NotifyPointerEnter(double x, double y)
    {
    }

    public void NotifyPointerMotion(uint timeMs, double x, double y)
    {
    }

    public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
    {
    }

    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
    }

    public void NotifyPointerLeave()
    {
    }

    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity)
    {
        _thread.Assert();
        if (_disposed)
        {
            return null;
        }

        var popup = _host is not null
            ? _host.Create()
            : new QuillVulkanUISurface(_device, _allocator, _pipeline, _textures, _atlas, null);
        if (!popup.Configure(Math.Max(1, anchor.Width), Math.Max(1, anchor.Height), _scale <= 0 ? 1.0 : _scale))
        {
            popup.Dispose();
            return null;
        }

        return popup;
    }

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_drawing)
        {
            _textures.CloseFrame();
        }

        _drawing = false;
        _host?.Forget(this);
        if (_lastPoint > 0)
        {
            _device.WaitFor(_lastPoint);
        }

        if (_canvas is null)
        {
            _renderer.Dispose();
        }
        else
        {
            _canvas.Dispose();
        }
        foreach (var frame in _frames)
        {
            frame.Dispose();
        }

        _frames.Clear();
        foreach (var (_, wrap) in _wraps)
        {
            Destroy(wrap);
        }

        _wraps.Clear();
        foreach (var wrap in _dropped)
        {
            Destroy(wrap);
        }

        _dropped.Clear();
        if (_target is not null && !ReferenceEquals(_target, _front) && !_target.IsDestroyed)
        {
            DestroyBuffer(_target);
        }

        if (_front is not null && !_front.IsDestroyed)
        {
            DestroyBuffer(_front);
        }

        foreach (var buffer in _retired.ToArray())
        {
            if (!buffer.IsDestroyed)
            {
                DestroyBuffer(buffer);
            }
        }

        _retired.Clear();
        foreach (var spare in _spares)
        {
            if (!spare.IsDestroyed)
            {
                DestroyBuffer(spare);
            }
        }

        _spares.Clear();
        _watched.Clear();
        _target = null;
        _front = null;
        _wholeDamage.Dispose();
        _observers.Destroyed(this);
    }

    private ReadOnlySpan<ulong> Modifiers => _host is null ? [] : _host.Modifiers;

    private QuillVulkanFrame AcquireFrame()
    {
        QuillVulkanFrame? oldest = null;
        foreach (var candidate in _frames)
        {
            if (candidate.IsIdle)
            {
                candidate.Reset();
                return candidate;
            }

            if (oldest is null || candidate.Point < oldest.Point)
            {
                oldest = candidate;
            }
        }

        if (_frames.Count < MaxFrames || oldest is null)
        {
            var fresh = new QuillVulkanFrame(_device, _pipeline);
            _frames.Add(fresh);
            return fresh;
        }

        _device.WaitFor(oldest.Point);
        oldest.Reset();
        return oldest;
    }

    private void WaitForReaders(IBuffer target)
    {
        if (!target.TryGetDmabuf(out var attributes))
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

            _device.FenceWait.Wait(fence);
            RenderFences.CloseFence(fence);
        }
    }

    private Target WrapOf(IBuffer buffer)
    {
        if (_wraps.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        if (!buffer.TryGetDmabuf(out var attributes))
        {
            throw new InvalidOperationException("a Quill Vulkan surface draws only into a dmabuf.");
        }

        var image = VulkanDeviceImage.TryImport(_device, attributes, ImageUsageFlags.ColorAttachmentBit)
            ?? throw new InvalidOperationException(
                $"{_device.DevicePath} would not import a {buffer.Width}x{buffer.Height} chrome buffer as a color attachment.");
        Framebuffer framebuffer;
        try
        {
            framebuffer = _pipeline.CreateFramebuffer(image.View, image.Width, image.Height);
        }
        catch
        {
            image.Dispose();
            throw;
        }

        var wrap = new Target { Image = image, Framebuffer = framebuffer };
        _wraps[buffer] = wrap;
        return wrap;
    }

    private void Retire(IBuffer buffer)
    {
        if (Reusable(buffer) && buffer.LockCount == 0)
        {
            _spares.Add(buffer);
            return;
        }

        if (buffer.LockCount == 0)
        {
            Discard(buffer);
            return;
        }

        _retired.Add(buffer);
        if (_watched.Add(buffer))
        {
            buffer.Released += () => OnReleased(buffer);
        }
    }

    private void OnReleased(IBuffer buffer)
    {
        if (!_retired.Remove(buffer) || buffer.IsDestroyed)
        {
            return;
        }

        if (!_disposed && Reusable(buffer))
        {
            _spares.Add(buffer);
            return;
        }

        Discard(buffer);
    }

    private bool Reusable(IBuffer buffer) =>
        !_disposed && _spares.Count < MaxSpares && _target is { } target && !ReferenceEquals(buffer, target)
        && buffer.Width == target.Width && buffer.Height == target.Height;

    private IBuffer? TakeSpare()
    {
        for (var i = 0; i < _spares.Count; i++)
        {
            var spare = _spares[i];
            if (spare.IsDestroyed)
            {
                _spares.RemoveAt(i--);
                continue;
            }

            if (spare.LockCount == 0)
            {
                _spares.RemoveAt(i);
                return spare;
            }
        }

        return null;
    }

    private void DropSpares()
    {
        foreach (var spare in _spares)
        {
            Discard(spare);
        }

        _spares.Clear();
    }

    private void Discard(IBuffer buffer)
    {
        _watched.Remove(buffer);
        if (_wraps.Remove(buffer, out var wrap))
        {
            if (wrap.LastUsed == 0 || _device.IsComplete(wrap.LastUsed))
            {
                Destroy(wrap);
            }
            else
            {
                _dropped.Add(wrap);
            }
        }

        if (!buffer.IsDestroyed)
        {
            DestroyBuffer(buffer);
        }
    }

    private void ReleaseDropped()
    {
        for (var i = _dropped.Count - 1; i >= 0; i--)
        {
            if (_device.IsComplete(_dropped[i].LastUsed))
            {
                Destroy(_dropped[i]);
                _dropped.RemoveAt(i);
            }
        }
    }

    private unsafe void Destroy(Target wrap)
    {
        _device.Api.DestroyFramebuffer(_device.Device, wrap.Framebuffer, null);
        wrap.Image.Dispose();
    }

    private static void DestroyBuffer(IBuffer buffer)
    {
        if (buffer is BufferBase concrete)
        {
            concrete.Destroy();
        }
    }
}
