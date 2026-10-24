using Basin.Capabilities;
using Basin.UI.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.Frames.MsStyles;

/// <summary>
/// An <see cref="IFrameRenderer"/> that paints an msstyles frame through SkiaSharp on an <see cref="ISkiaUISurface"/>.
/// It keeps the layout of one window, so each window must have its own instance.
/// </summary>
public sealed class MsStylesFrameRenderer : IFrameRenderer
{
    private readonly MsStylesFramePainter _painter;
    private readonly MsStylesResources _resources;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesFrameRenderer"/> class.
    /// </summary>
    /// <param name="style">The shared frame style. The renderer does not own it.</param>
    /// <param name="resources">The shared Skia resources. They must use the same <see cref="MsStylesImageCache"/> as <paramref name="style"/>.</param>
    /// <exception cref="ArgumentException">The resources use a different image cache from the style.</exception>
    public MsStylesFrameRenderer(MsStylesFrameStyle style, MsStylesResources resources)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(resources);
        if (!ReferenceEquals(resources.Images, style.Images))
        {
            throw new ArgumentException("The resources must be built over the style's image cache.", nameof(resources));
        }

        _painter = new MsStylesFramePainter(style) { TextMetrics = resources.Text };
        _resources = resources;
    }

    /// <summary>
    /// Gets the <see cref="ISkiaUISurface"/> type. A UI host that cannot give a Skia surface cannot host this renderer.
    /// </summary>
    public Type SurfaceContract => typeof(ISkiaUISurface);

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
        var skia = (ISkiaUISurface)surface;
        var scale = surface.Size.Scale;
        _painter.Layout(state, clientBox.Width, clientBox.Height);
        _painter.Paint(state, interaction, scale);
        var canvas = skia.BeginDraw();
        try
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Save();
            canvas.ResetMatrix();
            MsStylesSkiaPainter.Paint(canvas, _painter.List, _resources);
            canvas.Restore();
        }
        finally
        {
            skia.EndDraw();
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
