using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// A compositor-owned surface that a UI host draws into. The compositor shows its frames and forwards input in surface-local logical coordinates.
/// </summary>
public interface IUISurface : IDisposable
{
    /// <summary>
    /// Gets the logical size and the scale of the last successful <see cref="Configure"/>.
    /// </summary>
    UISurfaceSize Size { get; }

    /// <summary>
    /// Sets the logical size and the scale for the next drawn frame. The frame on screen stays valid until the next <see cref="TryAcquire"/>.
    /// </summary>
    /// <param name="logicalWidth">The width in logical pixels. It must be positive.</param>
    /// <param name="logicalHeight">The height in logical pixels. It must be positive.</param>
    /// <param name="scale">The scale from logical to buffer pixels. It must be positive.</param>
    /// <returns><see langword="false"/> when a value is not valid or the surface cannot change its size now.</returns>
    bool Configure(int logicalWidth, int logicalHeight, double scale);

    /// <summary>
    /// Tells the surface where the compositor placed it, in layout coordinates. The default implementation ignores the position.
    /// </summary>
    /// <param name="x">The left edge in layout coordinates.</param>
    /// <param name="y">The top edge in layout coordinates.</param>
    void SetPosition(double x, double y)
    {
    }

    /// <summary>
    /// Gets the x position in layout coordinates at which the toolkit placed the surface. The default is 0.
    /// </summary>
    double PositionX => 0;

    /// <summary>
    /// Gets the y position in layout coordinates at which the toolkit placed the surface. The default is 0.
    /// </summary>
    double PositionY => 0;

    /// <summary>
    /// Takes the newest drawn frame for display.
    /// </summary>
    /// <param name="frame">The frame. The caller must dispose it to release the buffer lock and the fence.</param>
    /// <returns><see langword="true"/> when the surface has a drawn frame. <see langword="false"/> when it has none or is disposed.</returns>
    bool TryAcquire(out UIFrame frame);

    /// <summary>
    /// Adds an observer for the damage and the destruction of the surface.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddObserver(IUISurfaceObserver observer);

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not present.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveObserver(IUISurfaceObserver observer);

    /// <summary>
    /// Gets a value indicating whether the surface takes input. When it is <see langword="false"/>, input goes to what is below. The default is <see langword="true"/>.
    /// </summary>
    bool AcceptsInput => true;

    /// <summary>
    /// Finds whether the surface takes input at a point.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <returns><see langword="true"/> when input at the point goes to this surface.</returns>
    bool AcceptsInputAt(double x, double y);

    /// <summary>
    /// Gets the cursor shape that the surface wants at a point.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <returns>An xcursor name, or <see langword="null"/> to let the compositor choose.</returns>
    string? CursorAt(double x, double y);

    /// <summary>
    /// Tells the surface that the pointer entered it.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    void NotifyPointerEnter(double x, double y);

    /// <summary>
    /// Tells the surface that the pointer moved.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    void NotifyPointerMotion(uint timeMs, double x, double y);

    /// <summary>
    /// Tells the surface that a pointer button changed.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The evdev button code, for example BTN_LEFT.</param>
    /// <param name="pressed"><see langword="true"/> when the button went down.</param>
    void NotifyPointerButton(uint timeMs, uint button, bool pressed);

    /// <summary>
    /// Tells the surface that the pointer scrolled.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The horizontal scroll distance, in wl_pointer axis units.</param>
    /// <param name="dy">The vertical scroll distance, in wl_pointer axis units.</param>
    void NotifyPointerAxis(uint timeMs, double dx, double dy);

    /// <summary>
    /// Tells the surface that the pointer left it.
    /// </summary>
    void NotifyPointerLeave();

    /// <summary>
    /// Gives keyboard focus to the surface. The default implementation ignores keyboard input.
    /// </summary>
    /// <param name="pressed">The evdev codes of the keys that are down at entry.</param>
    void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
    {
    }

    /// <summary>
    /// Tells the focused surface that a key changed.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The evdev key code.</param>
    /// <param name="pressed"><see langword="true"/> when the key went down.</param>
    void NotifyKey(uint timeMs, uint key, bool pressed)
    {
    }

    /// <summary>
    /// Tells the focused surface the modifier state, as wl_keyboard.modifiers sends it.
    /// </summary>
    /// <param name="depressed">The xkb mask of the depressed modifiers.</param>
    /// <param name="latched">The xkb mask of the latched modifiers.</param>
    /// <param name="locked">The xkb mask of the locked modifiers.</param>
    /// <param name="group">The xkb layout group.</param>
    void NotifyModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    /// <summary>
    /// Takes keyboard focus away from the surface.
    /// </summary>
    void NotifyKeyboardLeave()
    {
    }

    /// <summary>
    /// Tells the surface that a touch point went down on it. The default implementation ignores touch input.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id. It is unique while the point is down.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    void NotifyTouchDown(uint timeMs, int id, double x, double y)
    {
    }

    /// <summary>
    /// Tells the surface that a touch point moved.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    void NotifyTouchMotion(uint timeMs, int id, double x, double y)
    {
    }

    /// <summary>
    /// Tells the surface that a touch point went up.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    void NotifyTouchUp(uint timeMs, int id)
    {
    }

    /// <summary>
    /// Tells the surface that the compositor took every touch point away from it.
    /// </summary>
    void NotifyTouchCancel()
    {
    }

    /// <summary>
    /// Gets a value indicating whether the surface has a focused text field that needs an input method.
    /// The consumer reads it when focus changes. The default is <see langword="false"/>.
    /// </summary>
    bool WantsTextInput => false;

    /// <summary>
    /// Inserts text that the input method committed. The default implementation ignores it.
    /// </summary>
    /// <param name="text">The committed text.</param>
    void NotifyTextCommit(ReadOnlySpan<char> text)
    {
    }

    /// <summary>
    /// Sets the preedit text of the input method. An empty text clears it. The default implementation ignores it.
    /// </summary>
    /// <param name="text">The preedit text.</param>
    /// <param name="cursorBegin">The start of the cursor in the preedit, as text-input-v3 sends it.</param>
    /// <param name="cursorEnd">The end of the cursor in the preedit, as text-input-v3 sends it.</param>
    void NotifyPreedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd)
    {
    }

    /// <summary>
    /// Creates a popup surface that is placed against a box of this surface. The caller owns the popup and must dispose it.
    /// </summary>
    /// <param name="anchor">The anchor box in surface-local logical coordinates.</param>
    /// <param name="gravity">The direction in which the popup extends from the anchor.</param>
    /// <returns>The popup, or <see langword="null"/> when the host cannot make one.</returns>
    IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity);
}
