using Basin.Capabilities;
using Basin.Seat;

namespace Basin.Desktop;

/// <summary>
/// An input sink that moves a layout pointer and routes pointer events to the scene surface under it. It also gives keyboard focus to the surface under a button press.
/// </summary>
public class SceneSeatInput : SeatInputSink
{
    private readonly Basin.Scene.Scene _scene;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneSeatInput"/> class. It sets the seat's surface mapping to the scene when no mapping is set.
    /// </summary>
    /// <param name="seat">The seat that receives the events.</param>
    /// <param name="scene">The scene to hit-test.</param>
    /// <param name="layout">The layout that bounds the pointer.</param>
    public SceneSeatInput(Basin.Seat.Seat seat, Basin.Scene.Scene scene, OutputLayout layout)
        : base(seat, layout)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(layout);
        _scene = scene;
        seat.Pointer.MapToSurface ??= scene.TryMapToSurface;
        Pointer = new LayoutPointer(layout);
        Pointer.Reposition();
    }

    /// <summary>
    /// Gets the pointer position in layout coordinates.
    /// </summary>
    public LayoutPointer Pointer { get; }

    /// <summary>
    /// Gets or sets the output that absolute motion maps onto. When it is <see langword="null"/>, absolute motion maps onto the whole layout.
    /// </summary>
    public IOutput? Output { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a button press without an implicit grab gives keyboard focus to the surface under the pointer.
    /// </summary>
    public bool FocusOnButton { get; set; } = true;

    /// <inheritdoc/>
    public override bool PointerMotion(uint timeMs, double dx, double dy)
    {
        Pointer.Motion(dx, dy);
        Route(timeMs);
        return true;
    }

    /// <inheritdoc/>
    public override bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        Pointer.MotionAbsolute(Output, x / width, y / height);
        Route(timeMs);
        return true;
    }

    /// <inheritdoc/>
    public override bool PointerButton(uint timeMs, uint button, bool pressed)
    {
        if (pressed && FocusOnButton && !Seat.Pointer.HasImplicitGrab)
        {
            Seat.Keyboard.NotifyEnter(_scene.SurfaceAt(Pointer.X, Pointer.Y)?.Surface);
        }

        return base.PointerButton(timeMs, button, pressed);
    }

    /// <summary>
    /// Hit-tests the scene at the pointer position and sends pointer motion to the surface there, or clears the focus when no surface is hit.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    protected void Route(uint timeMs)
    {
        var hit = _scene.SurfaceAt(Pointer.X, Pointer.Y);
        Seat.Pointer.NotifyMotionAt(timeMs, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, Pointer.X, Pointer.Y);
    }
}
