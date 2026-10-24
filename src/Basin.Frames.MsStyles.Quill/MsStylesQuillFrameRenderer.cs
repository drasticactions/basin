using Basin.Capabilities;
using Basin.UI.Quill;
using Pixman;

namespace Basin.Frames.MsStyles.Quill;

/// <summary>
/// An <see cref="IFrameRenderer"/> that paints an msstyles frame through Prowl.Quill on an <see cref="IQuillUISurface"/>.
/// It keeps the layout of one window, so each window must have its own instance.
/// </summary>
public sealed class MsStylesQuillFrameRenderer : IFrameRenderer
{
    private readonly MsStylesFramePainter _painter;
    private readonly MsStylesQuillPainter _quill;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesQuillFrameRenderer"/> class.
    /// </summary>
    /// <param name="style">The shared frame style. The renderer does not own it.</param>
    /// <param name="painter">The shared Quill painter. It must use the same <see cref="MsStylesImageCache"/> as <paramref name="style"/>.</param>
    /// <exception cref="ArgumentException">The painter uses a different image cache from the style.</exception>
    public MsStylesQuillFrameRenderer(MsStylesFrameStyle style, MsStylesQuillPainter painter)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(painter);
        if (!ReferenceEquals(painter.Images, style.Images))
        {
            throw new ArgumentException("The painter must be built over the style's image cache.", nameof(painter));
        }

        _painter = new MsStylesFramePainter(style) { TextMetrics = painter.Text };
        _quill = painter;
    }

    /// <summary>
    /// Gets the <see cref="IQuillUISurface"/> type. A UI host that cannot give a Quill surface cannot host this renderer.
    /// </summary>
    public Type SurfaceContract => typeof(IQuillUISurface);

    /// <summary>
    /// Gets the per-window painter that lays out the frame and holds the draw list of the last draw.
    /// </summary>
    public MsStylesFramePainter Painter => _painter;

    /// <summary>
    /// Gets a value indicating whether every pixel of the last laid-out frame is opaque. It is false before the first layout.
    /// </summary>
    public bool OpaqueChrome => _painter.LaidOut && _painter.Opaque;

    /// <inheritdoc/>
    public FrameInsets Measure(in FrameState state, double scale) => _painter.Measure(state);

    /// <inheritdoc/>
    public FrameInsets Outsets(in FrameState state, double scale) => _painter.Outsets(state);

    /// <inheritdoc/>
    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var quill = (IQuillUISurface)surface;
        var scale = surface.Size.Scale;
        var canvas = quill.BeginDraw();
        try
        {
            _quill.Text.Canvas = canvas;
            _painter.Layout(state, clientBox.Width, clientBox.Height);
            _painter.Paint(state, interaction, scale);
            _quill.Paint(canvas, quill.Renderer, _painter.List, (float)scale);
        }
        finally
        {
            _quill.Text.Canvas = null;
            quill.EndDraw();
        }
    }

    /// <inheritdoc/>
    public FramePart PartAt(double x, double y, in FrameState state, double scale) => _painter.PartAt(x, y);

    /// <inheritdoc/>
    public Box PartBounds(FramePart part) => _painter.PartBounds(part);

    /// <inheritdoc/>
    public bool Animating(in FrameState state, in FrameInteraction interaction) => _painter.Animating(state, interaction);

    /// <summary>
    /// Gets a value indicating whether the look reads <see cref="FrameState.Placement"/>. Only the glass look does, because its streak follows the screen.
    /// </summary>
    public bool FollowsPlacement => _painter.Style.IsGlass;

    /// <inheritdoc/>
    public bool BackdropRegion(in FrameState state, double scale, PixmanRegion32 into)
    {
        ArgumentNullException.ThrowIfNull(into);
        return _painter.GlassBackdropRegion(into);
    }

    /// <inheritdoc/>
    public string? CursorFor(FramePart part) => part switch
    {
        FramePart.Top => "top_side",
        FramePart.Bottom => "bottom_side",
        FramePart.Left => "left_side",
        FramePart.Right => "right_side",
        FramePart.TopLeft => "top_left_corner",
        FramePart.TopRight => "top_right_corner",
        FramePart.BottomLeft => "bottom_left_corner",
        FramePart.BottomRight => "bottom_right_corner",
        _ => null,
    };
}
