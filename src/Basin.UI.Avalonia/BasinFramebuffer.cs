using Avalonia;
using Avalonia.Platform;
using Basin.Capabilities;
using Basin.Diagnostics;
using Pixman;

namespace Basin.UI.Avalonia;

internal sealed class BasinFramebuffer : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly PixmanRegion32 _whole = new();
    private readonly List<MemoryBuffer> _retired = [];
    private MemoryBuffer? _buffer;
    private int _width;
    private int _height;
    private double _scale = 1.0;
    private bool _produced;
    private bool _disposed;

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

        Retire();
        _buffer = new MemoryBuffer(physical.Width, physical.Height, DrmFormat.Argb8888);
        _produced = false;
        _width = logicalWidth;
        _height = logicalHeight;
        _scale = scale;
        return true;
    }

    public ILockedFramebuffer? Lock(Action onPublished)
    {
        _thread.Assert();
        if (_disposed || _buffer is null)
        {
            return null;
        }

        var buffer = _buffer;
        if (!buffer.BeginDataAccess(BufferDataAccess.Read | BufferDataAccess.Write, out var view))
        {
            return null;
        }

        return new Locked(this, buffer, view, onPublished);
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
                buffer.Destroy();
            }
        }

        _retired.Clear();
        _whole.Dispose();
    }

    private void Retire()
    {
        var buffer = _buffer;
        _buffer = null;
        if (buffer is null)
        {
            return;
        }

        if (buffer.LockCount == 0)
        {
            buffer.Destroy();
            return;
        }

        _retired.Add(buffer);
        buffer.Released += () =>
        {
            if (_retired.Remove(buffer) && !buffer.IsDestroyed)
            {
                buffer.Destroy();
            }
        };
    }

    private sealed class Locked : ILockedFramebuffer
    {
        private readonly BasinFramebuffer _owner;
        private readonly MemoryBuffer _buffer;
        private readonly Action _onPublished;
        private bool _disposed;

        public Locked(BasinFramebuffer owner, MemoryBuffer buffer, in BufferDataView view, Action onPublished)
        {
            _owner = owner;
            _buffer = buffer;
            _onPublished = onPublished;
            Address = view.Data;
            RowBytes = view.Stride;
            Size = new PixelSize(buffer.Width, buffer.Height);
            Dpi = new Vector(96, 96) * owner._scale;
        }

        public nint Address { get; }

        public PixelSize Size { get; }

        public int RowBytes { get; }

        public Vector Dpi { get; }

        public PixelFormat Format => PixelFormat.Bgra8888;

        public AlphaFormat AlphaFormat => AlphaFormat.Premul;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _buffer.EndDataAccess();
            _owner._produced = true;
            _onPublished();
        }
    }
}
