using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Pixman;
using Prowl.Quill;
using Silk.NET.OpenGLES;

namespace Basin.UI.Quill;

public sealed class QuillUISurface : IQuillUISurface
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly PixmanRegion32 _wholeDamage = new();
    private readonly UISurfaceObservers _observers = new();
    private const int MaxSpares = 2;

    private readonly List<IBuffer> _retired = [];
    private readonly List<IBuffer> _spares = [];
    private readonly HashSet<IBuffer> _watched = [];
    private readonly Dictionary<IBuffer, GlRenderTarget> _wraps = [];
    private readonly GlDevice _device;
    private readonly IAllocator _allocator;
    private readonly QuillGlProgram _program;
    private readonly QuillTextures _textures;
    private readonly FontAtlasSettings _atlas;
    private readonly QuillUIHost? _host;
    private readonly QuillCanvasRenderer _renderer;
    private Canvas? _canvas;

    private QuillGlContext.Scope _drawScope;
    private IBuffer? _target;
    private IBuffer? _front;
    private int _width;
    private int _height;
    private double _scale;
    private bool _drawing;
    private bool _canvasFrame;
    private bool _produced;
    private bool _disposed;

    internal QuillUISurface(
        GlDevice device,
        IAllocator allocator,
        QuillGlProgram program,
        QuillTextures textures,
        FontAtlasSettings atlas,
        QuillUIHost? host)
    {
        _device = device;
        _allocator = allocator;
        _program = program;
        _textures = textures;
        _atlas = atlas;
        _host = host;
        _renderer = new QuillCanvasRenderer(device.Gl, program, textures);
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

        using var current = Enter();
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
        _drawScope = Enter();
        GlRenderTarget native;
        try
        {
            native = WrapOf(target);
            WaitForReaders(target);
        }
        catch
        {
            _drawScope.Dispose();
            _drawScope = default;
            throw;
        }

        _renderer.SetTarget(native.Framebuffer, target.Width, target.Height);

        var gl = _device.Gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, native.Framebuffer);
        gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
        gl.Disable(EnableCap.ScissorTest);
        gl.ColorMask(true, true, true, true);
        gl.ClearColor(0f, 0f, 0f, 0f);
        gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _drawing = true;
        _canvasFrame = false;
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

        try
        {
            PublishWriteFence();
        }
        finally
        {
            _drawScope.Dispose();
            _drawScope = default;
        }

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
            _drawScope.Dispose();
            _drawScope = default;
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
            using var current = Enter();
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
            : new QuillUISurface(_device, _allocator, _program, _textures, _atlas, null);
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
            _drawScope.Dispose();
            _drawScope = default;
        }

        _drawing = false;
        _host?.Forget(this);
        using var current = Enter();
        if (_canvas is null)
        {
            _renderer.Dispose();
        }
        else
        {
            _canvas.Dispose();
        }
        foreach (var (_, native) in _wraps)
        {
            native.Dispose(_device);
        }

        _wraps.Clear();
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

    private void PublishWriteFence()
    {
        var fence = _target!.TryGetDmabuf(out var attributes) ? _device.ExportFence() : -1;
        if (fence < 0)
        {
            _device.Gl.Finish();
            _host?.ReportSync(fenced: false);
            return;
        }

        RenderFences.PublishFenceTo(attributes, forWrite: true, fence);
        RenderFences.CloseFence(fence);
        _host?.ReportSync(fenced: true);
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

            _device.WaitFence(fence);
            RenderFences.CloseFence(fence);
        }
    }

    private ReadOnlySpan<ulong> Modifiers => _host is null ? [] : _host.Modifiers;

    private QuillGlContext.Scope Enter() => _host?.Context.Enter() ?? default;

    private GlRenderTarget WrapOf(IBuffer buffer)
    {
        if (_wraps.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        var native = GlRenderTarget.Create(_device, buffer);
        _wraps[buffer] = native;
        return native;
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

        using var current = Enter();
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
        if (_wraps.Remove(buffer, out var native))
        {
            native.Dispose(_device);
        }

        if (!buffer.IsDestroyed)
        {
            DestroyBuffer(buffer);
        }
    }

    private static void DestroyBuffer(IBuffer buffer)
    {
        if (buffer is BufferBase concrete)
        {
            concrete.Destroy();
        }
    }
}
