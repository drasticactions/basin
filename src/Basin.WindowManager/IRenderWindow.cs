using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// The rendering half of a window, as <see cref="RenderContext.Windows"/> lists it. It carries no window-management request, so a render callback cannot make one by mistake.
/// </summary>
public interface IRenderWindow
{
    /// <summary>
    /// Gets the window's entry in the compositor's render list. The library creates it on first use and keeps it for the life of the window.
    /// </summary>
    WmNode Node { get; }

    /// <summary>
    /// Gets the size of the window content in logical pixels, without borders or decorations. It is zero before the first dimensions event.
    /// </summary>
    Size Dimensions { get; }

    /// <summary>
    /// Hides the window with its borders and decorations. It has no effect on a hidden window.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    void Hide();

    /// <summary>
    /// Shows a hidden window. A new window is shown until the manager hides it. Other windows can still cover it.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    void Show();

    /// <summary>
    /// Sets the borders that the compositor draws above the window content. The compositor draws no border while the window is fullscreen.
    /// </summary>
    /// <param name="edges">The edges that get a border. <see cref="Edges.None"/> removes the borders.</param>
    /// <param name="width">The border width in logical pixels. Zero removes the borders.</param>
    /// <param name="color">The premultiplied border color.</param>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative.</exception>
    void SetBorders(Edges edges, int width, WmColor color);

    /// <summary>
    /// Clips the window, with its borders and decorations, to a box. The compositor ignores the box while the window is fullscreen.
    /// </summary>
    /// <param name="box">The box relative to the window's top-left corner. A zero width or height turns clipping off.</param>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">The bound protocol version is lower than 2.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The width or height of <paramref name="box"/> is negative.</exception>
    void SetClipBox(Rect box);

    /// <summary>
    /// Clips only the window content to a box. Borders and decorations are not clipped, and borders surround the clipped content.
    /// </summary>
    /// <param name="box">The box relative to the window's top-left corner. A zero width or height turns content clipping off.</param>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">The bound protocol version is lower than 3.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The width or height of <paramref name="box"/> is negative.</exception>
    void SetContentClipBox(Rect box);
}
