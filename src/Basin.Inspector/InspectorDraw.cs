using Basin.UI.ImGui;

namespace Basin.Inspector;

/// <summary>
/// The frame state that the <see cref="InspectorOverlay"/> gives to each <see cref="IInspectorPanel"/> in one frame.
/// </summary>
public ref struct InspectorDraw
{
    private readonly ImGuiFrame _frame;

    internal InspectorDraw(ImGuiFrame frame, InspectorText text, InspectorOverlay overlay, long nowNanos)
    {
        _frame = frame;
        Text = text;
        Overlay = overlay;
        NowNanos = nowNanos;
    }

    /// <summary>
    /// Gets the text buffer of the overlay. The overlay resets it at the start of each frame, so a returned span is valid only in that frame.
    /// </summary>
    public InspectorText Text { get; }

    /// <summary>
    /// Gets the overlay that draws the panel.
    /// </summary>
    public InspectorOverlay Overlay { get; }

    /// <summary>
    /// Gets the clock reading of the overlay at the start of the frame, in nanoseconds. Only differences between two readings have a meaning.
    /// </summary>
    public long NowNanos { get; }

    /// <summary>
    /// Gets the width of the overlay surface in logical pixels.
    /// </summary>
    public readonly int Width => _frame.Width;

    /// <summary>
    /// Gets the height of the overlay surface in logical pixels.
    /// </summary>
    public readonly int Height => _frame.Height;

    /// <summary>
    /// Gets the scale from logical to buffer pixels.
    /// </summary>
    public readonly double Scale => _frame.Scale;

    /// <summary>
    /// Gets the time since the last frame in seconds. It is never 0.
    /// </summary>
    public readonly float DeltaTime => _frame.DeltaTime;

    /// <summary>
    /// Keeps the overlay drawing at a rate after this frame. The request lasts for one frame, so a live panel calls it on each draw.
    /// </summary>
    /// <param name="hz">The frames per second. A value of 0 or less has no effect.</param>
    public readonly void RequestRefresh(int hz) => _frame.RequestRefresh(hz);
}
