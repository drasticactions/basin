namespace Basin.Scene;

/// <summary>
/// Shows content inside a target box when the content's size does not match, with black bars behind it and a scaling transform on it.
/// </summary>
public sealed class SceneFit
{
    private readonly SceneRect _bars;
    private readonly SceneTransform _transform;
    private TextureFilter? _filter;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneFit"/> class. It adds a root tree to the parent, with hidden bars and an identity transform.
    /// </summary>
    /// <param name="parent">The tree to add the fit nodes to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
    public SceneFit(SceneTree parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Root = new SceneTree(parent);
        _bars = new SceneRect(Root, 0, 0, new RenderColor(0f, 0f, 0f, 1f)) { Enabled = false, BlocksInput = true };
        _transform = new SceneTransform(Root);
    }

    /// <summary>
    /// Gets the tree that holds the bars and the content. Position and stack this tree.
    /// </summary>
    public SceneTree Root { get; }

    /// <summary>
    /// Gets the tree to put the content in. It is the transform that scales and positions the content.
    /// </summary>
    public SceneTree Content => _transform;

    /// <summary>
    /// Gets a value indicating whether the last <see cref="Update(in Box, ValueTuple{int, int}, ValueTuple{int, int}, SceneFitMode?)"/> scaled or moved the content.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the target box from the last update, in the parent's logical coordinates.
    /// </summary>
    public Box Target { get; private set; }

    /// <summary>
    /// Gets the box where the content shows after the fit, in the parent's logical coordinates.
    /// </summary>
    public Box ContentBox { get; private set; }

    /// <summary>
    /// Gets the horizontal scale of the content. It is 1 while the fit is inactive.
    /// </summary>
    public double ContentScaleX { get; private set; } = 1.0;

    /// <summary>
    /// Gets the vertical scale of the content. It is 1 while the fit is inactive.
    /// </summary>
    public double ContentScaleY { get; private set; } = 1.0;

    /// <summary>
    /// Gets or sets the texture filter for scaled content. It applies only while the fit is active.
    /// </summary>
    public TextureFilter? Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            _transform.Filter = IsActive ? value : null;
        }
    }

    /// <summary>
    /// Decides the fit mode for a window whose committed size differs from the size it was configured to.
    /// </summary>
    /// <param name="configured">The size the compositor asked for, in logical pixels.</param>
    /// <param name="committed">The size the client committed, in logical pixels.</param>
    /// <param name="state">The window state.</param>
    /// <param name="policy">The user setting.</param>
    /// <returns>The mode, or <see langword="null"/> when the policy is off, the window floats, a size is not positive, or the sizes match.</returns>
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
            SceneFitPolicy.Integer => SceneFitMode.Integer,
            SceneFitPolicy.Fill => SceneFitMode.Fill,
            SceneFitPolicy.Stretch => SceneFitMode.Stretch,
            _ => state == SceneFitState.Fullscreen ? SceneFitMode.Fit : SceneFitMode.Center,
        };
    }

    /// <summary>
    /// Computes where content of a given size goes inside a target box, centered.
    /// </summary>
    /// <param name="target">The box to place the content in.</param>
    /// <param name="content">The content size, in logical pixels.</param>
    /// <param name="mode">How to scale the content.</param>
    /// <returns>The horizontal scale, the vertical scale and the placed box. Only <see cref="SceneFitMode.Stretch"/> gives two different scales. An empty input gives scale 1 at the target origin.</returns>
    public static (double ScaleX, double ScaleY, Box Box) Place(in Box target, (int Width, int Height) content, SceneFitMode mode)
    {
        if (content.Width <= 0 || content.Height <= 0 || target.IsEmpty)
        {
            return (1.0, 1.0, new Box(target.X, target.Y, Math.Max(0, content.Width), Math.Max(0, content.Height)));
        }

        if (mode == SceneFitMode.Stretch)
        {
            return (target.Width / (double)content.Width, target.Height / (double)content.Height, target);
        }

        var ratioX = target.Width / (double)content.Width;
        var ratioY = target.Height / (double)content.Height;
        var fit = Math.Min(ratioX, ratioY);
        var scale = mode switch
        {
            SceneFitMode.Fit => fit,
            SceneFitMode.Fill => Math.Max(ratioX, ratioY),
            SceneFitMode.Integer => fit > 1.0 ? Math.Floor(fit) : fit,
            _ => Math.Min(1.0, fit),
        };
        var width = (int)Math.Round(content.Width * scale);
        var height = (int)Math.Round(content.Height * scale);
        var x = target.X + ((target.Width - width) / 2);
        var y = target.Y + ((target.Height - height) / 2);
        return (scale, scale, new Box(x, y, width, height));
    }

    /// <summary>
    /// Places content whose drawn size and aspect ratio are the same.
    /// </summary>
    /// <param name="target">The box to place the content in, in the parent's logical coordinates.</param>
    /// <param name="content">The content size, in logical pixels.</param>
    /// <param name="mode">The fit mode, or <see langword="null"/> to show the content unscaled at the target origin.</param>
    public void Update(in Box target, (int Width, int Height) content, SceneFitMode? mode) =>
        Update(target, content, content, mode);

    /// <summary>
    /// Places content, scales the transform and shows the bars. The fit becomes inactive when there is no mode, a size is not positive, or the content already fills the target.
    /// </summary>
    /// <param name="target">The box to place the content in, in the parent's logical coordinates.</param>
    /// <param name="drawn">The size that the content draws at, in logical pixels.</param>
    /// <param name="aspect">The size whose aspect ratio decides the placement.</param>
    /// <param name="mode">The fit mode, or <see langword="null"/> to show the content unscaled at the target origin.</param>
    public void Update(in Box target, (int Width, int Height) drawn, (int Width, int Height) aspect, SceneFitMode? mode)
    {
        Target = target;
        if (mode is not { } active || drawn.Width <= 0 || drawn.Height <= 0 || aspect.Width <= 0 || aspect.Height <= 0
            || target.IsEmpty)
        {
            Deactivate(target, drawn);
            return;
        }

        var (_, _, box) = Place(target, aspect, active);
        if (box == target && drawn.Width == target.Width && drawn.Height == target.Height)
        {
            Deactivate(target, drawn);
            return;
        }

        var scaleX = box.Width / (double)drawn.Width;
        var scaleY = box.Height / (double)drawn.Height;
        IsActive = true;
        Root.ClipBox = active == SceneFitMode.Fill ? target : default;
        ContentScaleX = scaleX;
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

    /// <summary>
    /// Maps a point in the parent's coordinates into the content's own coordinates, undoing the fit.
    /// </summary>
    /// <param name="x">The horizontal coordinate, in the parent's logical pixels.</param>
    /// <param name="y">The vertical coordinate, in the parent's logical pixels.</param>
    /// <returns>The point in content logical pixels.</returns>
    public (double X, double Y) ToContent(double x, double y) =>
        IsActive
            ? ((x - ContentBox.X) / ContentScaleX, (y - ContentBox.Y) / ContentScaleY)
            : (x - ContentBox.X, y - ContentBox.Y);

    /// <summary>
    /// Destroys <see cref="Root"/> and every node under it. A second call does nothing.
    /// </summary>
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
        Root.ClipBox = default;
        ContentScaleX = 1.0;
        ContentScaleY = 1.0;
        ContentBox = new Box(target.X, target.Y, Math.Max(0, content.Width), Math.Max(0, content.Height));
        _bars.Enabled = false;
        _transform.SetPosition(target.X, target.Y);
        _transform.Matrix = RenderTransform.Identity;
        _transform.Filter = null;
    }
}
