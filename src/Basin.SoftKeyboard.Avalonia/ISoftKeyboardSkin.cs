using Avalonia;
using Avalonia.Media;

namespace Basin.SoftKeyboard.Avalonia;

/// <summary>
/// Draws a soft keyboard for a <see cref="SoftKeyboardCanvas"/>. The canvas calls the methods in declaration order on each frame.
/// </summary>
public interface ISoftKeyboardSkin
{
    /// <summary>
    /// Draws the background first. The canvas calls it also when it has no engine.
    /// </summary>
    /// <param name="context">The context to draw into.</param>
    /// <param name="bounds">The canvas bounds, with the origin at 0, 0.</param>
    void DrawBackground(DrawingContext context, Rect bounds);

    /// <summary>
    /// Draws one visible key. The canvas calls it once for each visible key that has a plain cap.
    /// </summary>
    /// <param name="context">The context to draw into.</param>
    /// <param name="key">The key's cell, bounds in the canvas, shown cap, label and state flags.</param>
    void DrawKey(DrawingContext context, in SoftKeyDraw key);

    /// <summary>
    /// Draws above the keys and below the long-press popup, for example a trace path.
    /// </summary>
    /// <param name="context">The context to draw into.</param>
    /// <param name="bounds">The canvas bounds, with the origin at 0, 0.</param>
    void DrawOverKeys(DrawingContext context, Rect bounds);

    /// <summary>
    /// Draws the open long-press list. The canvas calls it last, and only while a list is open.
    /// </summary>
    /// <param name="context">The context to draw into.</param>
    /// <param name="popup">The list geometry, entries, highlighted entry and keymap top.</param>
    void DrawPopup(DrawingContext context, in SoftKeyPopupDraw popup);
}
