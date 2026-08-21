using Basin.Capabilities;
using Pixman;

namespace Basin.Scene;

public sealed class UISurfaceNode : IUISurfaceObserver, IDisposable
{
    private readonly SceneBuffer _node;
    private readonly IUIHost? _host;
    private readonly UISurfaceIndex? _index;
    private readonly bool _ownsSurface;
    private IUISurface? _surface;
    private UIFrame _shown;
    private bool _hasShown;
    private bool _faulted;
    private bool _disposed;
    private int _width;
    private int _height;
    private double _scale = 1.0;

    public UISurfaceNode(SceneTree parent, IUISurface surface, UISurfaceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(surface);

        _node = new SceneBuffer(parent) { Enabled = false };
        _surface = surface;
        _index = index;
        _width = surface.Size.Width;
        _height = surface.Size.Height;
        _scale = surface.Size.Scale;
        surface.AddObserver(this);
        _index?.Add(this);
    }

    public UISurfaceNode(SceneTree parent, IUIHost host, UISurfaceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(host);

        _node = new SceneBuffer(parent) { Enabled = false };
        _host = host;
        _index = index;
        _ownsSurface = true;
    }

    public SceneBuffer Node => _node;

    public IUISurface? Surface => _surface;

    public int X { get; private set; }

    public int Y { get; private set; }

    public int Width => _width;

    public int Height => _height;

    public double Scale => _scale;

    public bool IsFaulted => _faulted;

    public bool AutoEnable { get; set; } = true;

    public UITargetKind? Target { get; init; }

    public DrmDeviceInfo? Device { get; init; }

    public bool Enabled
    {
        get => _node.Enabled;
        set => _node.Enabled = value;
    }

    public bool InputEnabled
    {
        get => _node.InputEnabled;
        set => _node.InputEnabled = value;
    }

    public bool PreciseDamage
    {
        get => _node.PreciseDamage;
        set => _node.PreciseDamage = value;
    }

    public event Action<Exception>? Faulted;

    public void SetPosition(int x, int y)
    {
        X = x;
        Y = y;
        _node.SetPosition(x, y);
    }

    public bool Configure(int width, int height, double scale)
    {
        if (_disposed || _faulted || width <= 0 || height <= 0 || scale <= 0)
        {
            return false;
        }

        if (_surface is null)
        {
            if (!_ownsSurface || _host is null)
            {
                return false;
            }

            IUISurface? created;
            try
            {
                created = _host.CreateSurface(new UISurfaceOptions
                {
                    Target = Target ?? ((_host.Produces & UITargetKind.Dmabuf) != 0
                        ? UITargetKind.Dmabuf
                        : UITargetKind.Memory),
                    Width = width,
                    Height = height,
                    Scale = scale,
                    Device = Device,
                });
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
            {
                Fault(error);
                return false;
            }

            if (created is null)
            {
                Fault(new InvalidOperationException("The UI host declined to create a surface."));
                return false;
            }

            _surface = created;
            _surface.AddObserver(this);
            _index?.Add(this);
        }
        else if (!_surface.Configure(width, height, scale))
        {
            return false;
        }

        _width = width;
        _height = height;
        _scale = scale;
        _node.DestinationWidth = width;
        _node.DestinationHeight = height;
        return true;
    }

    public bool Publish()
    {
        if (_disposed || _faulted || _surface is null || !_surface.TryAcquire(out var frame))
        {
            return false;
        }

        if (frame.Buffer is null)
        {
            frame.Dispose();
            return false;
        }

        var size = _surface.Size;
        _width = size.Width;
        _height = size.Height;
        _scale = size.Scale;
        _node.SetBuffer(frame.Buffer);
        _node.SourceBox = OutputScaling.ToPhysical(new Box(0, 0, size.Width, size.Height), size.Scale);
        _node.DestinationWidth = size.Width;
        _node.DestinationHeight = size.Height;
        if (AutoEnable)
        {
            _node.Enabled = true;
        }

        _node.NotifyContentChanged();

        if (_hasShown)
        {
            _shown.Dispose();
        }

        _shown = frame;
        _hasShown = true;
        return true;
    }

    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage) => Publish();

    public void OnSurfaceDestroyed(IUISurface surface)
    {
        if (ReferenceEquals(surface, _surface))
        {
            _index?.Remove(this);
            _surface = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _index?.Remove(this);
        _surface?.RemoveObserver(this);
        _node.SetBuffer(null);
        if (_hasShown)
        {
            _shown.Dispose();
            _hasShown = false;
        }

        if (!_node.IsDestroyed)
        {
            _node.Destroy();
        }

        if (_ownsSurface)
        {
            _surface?.Dispose();
        }

        _surface = null;
    }

    private void Fault(Exception error)
    {
        _faulted = true;
        Faulted?.Invoke(error);
    }
}
