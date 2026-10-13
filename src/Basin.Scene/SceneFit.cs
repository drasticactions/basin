namespace Basin.Scene;

public sealed class SceneFit
{
    private readonly SceneRect _bars;
    private readonly SceneTransform _transform;
    private TextureFilter? _filter;

    public SceneFit(SceneTree parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Root = new SceneTree(parent);
        _bars = new SceneRect(Root, 0, 0, new RenderColor(0f, 0f, 0f, 1f)) { Enabled = false, BlocksInput = true };
        _transform = new SceneTransform(Root);
    }

    public SceneTree Root { get; }

    public SceneTree Content => _transform;

    public bool IsActive { get; private set; }

    public Box Target { get; private set; }

    public Box ContentBox { get; private set; }

    public double ContentScale { get; private set; } = 1.0;

    public double ContentScaleY { get; private set; } = 1.0;

    public TextureFilter? Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            _transform.Filter = IsActive ? value : null;
        }
    }

    public static SceneFitMode? Needed(
        (int Width, int Height) configured, (int Width, int Height) committed, SceneFitState state, SceneFitPolicy policy)
    {
        if (policy == SceneFitPolicy.Off || state == SceneFitState.Floating)
        {
            return null;
        }

        if (configured.Width <= 0 || configured.Height <= 0 || committed.Width <= 0 || committed.Height <= 0
            || configured == committed)
        {
            return null;
        }

        return policy switch
        {
            SceneFitPolicy.Fit => SceneFitMode.Fit,
            SceneFitPolicy.Center => SceneFitMode.Center,
            _ => state == SceneFitState.Fullscreen ? SceneFitMode.Fit : SceneFitMode.Center,
        };
    }

    public static (double Scale, Box Box) Place(in Box target, (int Width, int Height) content, SceneFitMode mode)
    {
        if (content.Width <= 0 || content.Height <= 0 || target.IsEmpty)
        {
            return (1.0, new Box(target.X, target.Y, Math.Max(0, content.Width), Math.Max(0, content.Height)));
        }

        var fit = Math.Min(target.Width / (double)content.Width, target.Height / (double)content.Height);
        var scale = mode == SceneFitMode.Fit ? fit : Math.Min(1.0, fit);
        var width = (int)Math.Round(content.Width * scale);
        var height = (int)Math.Round(content.Height * scale);
        var x = target.X + ((target.Width - width) / 2);
        var y = target.Y + ((target.Height - height) / 2);
        return (scale, new Box(x, y, width, height));
    }

    public void Update(in Box target, (int Width, int Height) content, SceneFitMode? mode) =>
        Update(target, content, content, mode);

    public void Update(in Box target, (int Width, int Height) drawn, (int Width, int Height) aspect, SceneFitMode? mode)
    {
        Target = target;
        if (mode is not { } active || drawn.Width <= 0 || drawn.Height <= 0 || aspect.Width <= 0 || aspect.Height <= 0
            || target.IsEmpty)
        {
            Deactivate(target, drawn);
            return;
        }

        var (_, box) = Place(target, aspect, active);
        if (box == target && drawn.Width == target.Width && drawn.Height == target.Height)
        {
            Deactivate(target, drawn);
            return;
        }

        var scaleX = box.Width / (double)drawn.Width;
        var scaleY = box.Height / (double)drawn.Height;
        IsActive = true;
        ContentScale = scaleX;
        ContentScaleY = scaleY;
        ContentBox = box;
        _bars.SetPosition(target.X, target.Y);
        _bars.Width = target.Width;
        _bars.Height = target.Height;
        _bars.Enabled = true;
        _transform.SetPosition(box.X, box.Y);
        _transform.Matrix = scaleX == 1.0 && scaleY == 1.0 ? RenderTransform.Identity : RenderTransform.Scale(scaleX, scaleY);
        _transform.Filter = _filter;
    }

    public (double X, double Y) ToContent(double x, double y) =>
        IsActive
            ? ((x - ContentBox.X) / ContentScale, (y - ContentBox.Y) / ContentScaleY)
            : (x - ContentBox.X, y - ContentBox.Y);

    public void Destroy()
    {
        if (!Root.IsDestroyed)
        {
            Root.Destroy();
        }
    }

    private void Deactivate(in Box target, (int Width, int Height) content)
    {
        IsActive = false;
        ContentScale = 1.0;
        ContentScaleY = 1.0;
        ContentBox = new Box(target.X, target.Y, Math.Max(0, content.Width), Math.Max(0, content.Height));
        _bars.Enabled = false;
        _transform.SetPosition(target.X, target.Y);
        _transform.Matrix = RenderTransform.Identity;
        _transform.Filter = null;
    }
}
