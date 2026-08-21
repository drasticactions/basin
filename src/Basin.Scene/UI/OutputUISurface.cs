using Basin.Capabilities;

namespace Basin.Scene;

public sealed class OutputUISurface : IDisposable
{
    private readonly UISurfaceNode _node;
    private Box _bounds;
    private double _scale = 1.0;
    private bool _placed;
    private bool _disposed;

    public OutputUISurface(SceneTree parent, IUIHost host, UISurfaceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(host);

        _node = new UISurfaceNode(parent, host, index);
    }

    public UISurfaceNode Node => _node;

    public IUISurface? Surface => _node.Surface;

    public Box Bounds => _bounds;

    public double Scale => _scale;

    public bool IsRealized => _node.Surface is not null;

    public bool IsPlaced => _placed;

    public bool IsFaulted => _node.IsFaulted;

    public bool AutoEnable
    {
        get => _node.AutoEnable;
        set => _node.AutoEnable = value;
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

    public Func<Box, double, Box>? Anchor { get; set; }

    public event Action<IUISurface>? Realized;

    public bool Enabled
    {
        get => _node.Enabled;
        set => _node.Enabled = value;
    }

    public bool Place(in Box outputBox, double scale) =>
        PlaceAt(Anchor is { } anchor ? anchor(outputBox, scale) : outputBox, scale);

    public bool PlaceAt(in Box box, double scale)
    {
        if (_disposed || box.Width <= 0 || box.Height <= 0 || scale <= 0)
        {
            return false;
        }

        var realized = IsRealized;
        _node.SetPosition(box.X, box.Y);
        if (!_node.Configure(box.Width, box.Height, scale))
        {
            return false;
        }

        _bounds = box;
        _scale = scale;
        _placed = true;

        var (sceneX, sceneY) = _node.Node.ScenePosition;
        _node.Surface?.SetPosition(sceneX, sceneY);

        if (!realized && _node.Surface is { } surface)
        {
            Realized?.Invoke(surface);
        }

        return true;
    }

    public bool Publish() => _node.Publish();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _node.Dispose();
    }
}
