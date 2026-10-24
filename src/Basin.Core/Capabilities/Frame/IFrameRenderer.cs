namespace Basin.Capabilities;

/// <summary>
/// A look for server-side window frames. It measures the frame, draws it into a UI surface and says which part is at a
/// point. An implementation can keep the layout of the frame it drew last for hit testing, so a consumer gives each
/// window its own renderer. Coordinates are logical pixels on the frame surface, outsets included.
/// </summary>
public interface IFrameRenderer
{
    /// <summary>
    /// Gets the <see cref="IUISurface"/> type that <see cref="Draw"/> needs. A UI host that cannot give that type cannot host the renderer.
    /// </summary>
    Type SurfaceContract => typeof(IUISurface);

    /// <summary>
    /// Measures the frame thickness around the client area.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The insets in logical pixels.</returns>
    FrameInsets Measure(in FrameState state, double scale);

    /// <summary>
    /// Measures a drawn margin outside the insets, for example a shadow. A point in the outsets is not part of the window.
    /// The default is no margin.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The outsets in logical pixels.</returns>
    FrameInsets Outsets(in FrameState state, double scale) => default;

    /// <summary>
    /// Draws the whole frame into a surface that covers the client area, the insets and the outsets.
    /// </summary>
    /// <param name="surface">The surface to draw into. It is of the <see cref="SurfaceContract"/> type.</param>
    /// <param name="clientBox">The client area in surface coordinates. The renderer must leave it clear.</param>
    /// <param name="state">The window state.</param>
    /// <param name="interaction">The pointer state and the change times.</param>
    void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction);

    /// <summary>
    /// Finds the frame part at a point of the last drawn frame.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The part, or <see cref="FramePart.None"/> to let input through to the window below.</returns>
    FramePart PartAt(double x, double y, in FrameState state, double scale);

    /// <summary>
    /// Gets a value indicating whether every pixel of the frame is opaque. The default is false.
    /// </summary>
    bool OpaqueChrome => false;

    /// <summary>
    /// Tells whether a draw at a later time gives different pixels, for example during a fade. The default is false.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="interaction">The pointer state and the change times.</param>
    /// <returns><see langword="true"/> to have the frame drawn again on each frame clock tick.</returns>
    bool Animating(in FrameState state, in FrameInteraction interaction) => false;

    /// <summary>
    /// Gets a value indicating whether the look reads <see cref="FrameState.Placement"/>. The frame redraws on a move only
    /// when this is true. The default is false.
    /// </summary>
    bool FollowsPlacement => false;

    /// <summary>
    /// Fills the region where a backdrop effect, for example blur, shows through the frame. The default fills nothing.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="into">The region to fill, in surface coordinates. The caller clears it first.</param>
    /// <returns><see langword="true"/> when the frame wants a backdrop effect.</returns>
    bool BackdropRegion(in FrameState state, double scale, Pixman.PixmanRegion32 into) => false;

    /// <summary>
    /// Describes the frame as a shape for a shaped backdrop effect, for example glass. The default gives no shape.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="shape">The shape when the call returns <see langword="true"/>, otherwise the default value.</param>
    /// <returns><see langword="true"/> when the frame has a shape to publish.</returns>
    bool BackdropShape(in FrameState state, double scale, out FrameBackdropShape shape)
    {
        shape = default;
        return false;
    }

    /// <summary>
    /// Names the cursor to show over a part. The default is <see langword="null"/>.
    /// </summary>
    /// <param name="part">The part under the pointer.</param>
    /// <returns>A cursor shape name, or <see langword="null"/> to leave the cursor to the caller.</returns>
    string? CursorFor(FramePart part) => null;

    /// <summary>
    /// Gives the area that a part covered in the last draw. A hover or press change on the part damages only that area.
    /// </summary>
    /// <param name="part">The part to look up.</param>
    /// <returns>The bounds in surface coordinates. An empty box, the default, adds no damage, so the change is not redrawn.</returns>
    Box PartBounds(FramePart part) => default;

    /// <summary>
    /// Measures the window menu. An empty size, the default, makes the frame raise <see cref="FrameActionKind.ShowMenu"/> for the consumer instead.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The menu size.</returns>
    UISurfaceSize MeasureMenu(in FrameState state, double scale) => default;

    /// <summary>
    /// Draws the window menu into its popup surface. The default draws nothing.
    /// </summary>
    /// <param name="surface">The menu surface.</param>
    /// <param name="state">The window state.</param>
    /// <param name="hotItem">The item under the pointer, or -1 for none.</param>
    void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
    }

    /// <summary>
    /// Finds the menu item at a point on the menu surface.
    /// </summary>
    /// <param name="x">The horizontal position on the menu surface.</param>
    /// <param name="y">The vertical position on the menu surface.</param>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The item index, or -1 when no item is at the point. The default is -1.</returns>
    int MenuItemAt(double x, double y, in FrameState state, double scale) => -1;

    /// <summary>
    /// Gives the action that a menu item asks for when it is chosen.
    /// </summary>
    /// <param name="item">The item index that <see cref="MenuItemAt"/> returned.</param>
    /// <param name="state">The window state.</param>
    /// <returns>The action, or <see langword="null"/> when the item does nothing. The default is <see langword="null"/>.</returns>
    FrameAction? MenuItemAction(int item, in FrameState state) => null;
}
