namespace Basin.Scene;

/// <summary>
/// The zoom of an output that a compositor draws by rendering the scene into its own buffer instead of through a <see cref="SceneOutput"/>, such as an output on a secondary card.
/// It joins the scene's zoomable outputs, so <see cref="OutputZoom"/> magnifies it with the others, and it gives the projection to render with.
/// </summary>
public sealed class ZoomView : IZoomable, IDisposable
{
    private readonly Scene _scene;
    private double _factor = 1.0;
    private double _centerX;
    private double _centerY;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZoomView"/> class and adds it to the scene's zoomable outputs.
    /// </summary>
    /// <param name="scene">The scene that the output shows.</param>
    /// <param name="output">The output.</param>
    public ZoomView(Scene scene, IOutput output)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(output);
        _scene = scene;
        Output = output;
        scene.AttachZoomable(this);
    }

    /// <inheritdoc/>
    public IOutput Output { get; }

    /// <summary>
    /// Gets or sets the position of the output's top-left corner in layout coordinates. Set it before each render.
    /// </summary>
    public Point Position { get; set; }

    /// <inheritdoc/>
    public double ZoomFactor => _factor;

    /// <inheritdoc/>
    public (double X, double Y, double Width, double Height) LogicalArea
    {
        get
        {
            var (width, height) = ZoomGeometry.LogicalSize(Output);
            return (Position.X, Position.Y, width, height);
        }
    }

    /// <inheritdoc/>
    public (double X, double Y, double Width, double Height) ZoomArea => ZoomGeometry.Area(Output, Position, _factor, _centerX, _centerY);

    /// <summary>
    /// Gets the projection that renders the visible area into the output's mode.
    /// </summary>
    public OutputProjection Projection => ZoomGeometry.Project(Output, Position, _factor, _centerX, _centerY);

    /// <summary>
    /// Occurs after the factor or the visible area changed, so the owner can repaint.
    /// </summary>
    public event Action? Changed;

    /// <inheritdoc/>
    public void SetZoom(double factor, double centerX, double centerY)
    {
        if (_disposed)
        {
            return;
        }

        factor = ZoomGeometry.ClampFactor(factor);
        (centerX, centerY) = ZoomGeometry.ClampCenter(Output, Position, factor, centerX, centerY);
        if (factor == _factor && (factor == 1.0 || (centerX == _centerX && centerY == _centerY)))
        {
            return;
        }

        (_factor, _centerX, _centerY) = (factor, centerX, centerY);
        Changed?.Invoke();
    }

    /// <summary>
    /// Leaves the scene's zoomable outputs. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene.DetachZoomable(this);
    }
}
