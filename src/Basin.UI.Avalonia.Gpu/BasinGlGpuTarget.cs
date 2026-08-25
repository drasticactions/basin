using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;
using Avalonia.Platform;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Pixman;
using MesaEglImage = Mesa.Egl.EglImage;

namespace Basin.UI.Avalonia;

internal sealed class BasinGlGpuTarget : IAvaloniaGpuTarget
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly PixmanRegion32 _whole = new();
    private readonly List<IBuffer> _retired = [];
    private readonly GlDevice _device;
    private readonly IAllocator _allocator;
    private readonly ulong[] _modifiers;
    private IBuffer? _buffer;
    private MesaEglImage? _image;
    private int _width;
    private int _height;
    private double _scale = 1.0;
    private bool _produced;
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
        if (logicalWidth == _width && logicalHeight == _height && scale == _scale && _buffer is not null)
        {
            return true;
        }

        var physical = OutputScaling.ToPhysical(new Box(0, 0, logicalWidth, logicalHeight), scale);
        if (physical.IsEmpty)
        {
            return false;
        }

        var allocated = _allocator.Allocate(
            physical.Width, physical.Height, DrmFormat.Argb8888, _modifiers, BufferUse.Render);
        if (allocated is null)
        {
            return false;
        }

        if (!allocated.TryGetDmabuf(out var attributes))
        {
            Destroy(allocated);
            return false;
        }

        var image = _device.ImportDmabufImage(attributes);
        if (image is null)
        {
            Destroy(allocated);
            return false;
        }

        Retire();
        _buffer = allocated;
        _image = image;
        _produced = false;
        _width = logicalWidth;
        _height = logicalHeight;
        _scale = scale;
        return true;
    }

    public bool TryAcquire(out UIFrame frame)
    {
        _thread.Assert();
        if (_disposed || !_produced || _buffer is null)
        {
            frame = default;
            return false;
        }

        frame = new UIFrame(_buffer.Lock(), damage: null);
        return true;
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
        Retire();
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

    private void Retire()
    {
        _image?.Dispose();
        _image = null;
        var buffer = _buffer;
        _buffer = null;
        if (buffer is null)
        {
            return;
        }

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
                owner._produced = true;
                onFramePublished();
            };
        }

        public override IGlPlatformSurfaceRenderingSession BeginDrawCore(
            IRenderTarget.RenderTargetSceneInfo sceneInfo)
        {
            if (_owner._disposed || _owner._buffer is not { } buffer || _owner._image is not { } image)
            {
                throw new RenderTargetCorruptedException();
            }

            return BeginDraw(
                image.Handle,
                new PixelSize(buffer.Width, buffer.Height),
                _owner._scale,
                _onPublished);
        }
    }
}
