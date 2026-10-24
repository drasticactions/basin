namespace Basin.Inspector;

/// <summary>
/// One window of the <see cref="InspectorOverlay"/>. The overlay calls <see cref="Draw(ref InspectorDraw)"/> only while the panel is shown.
/// </summary>
public interface IInspectorPanel
{
    /// <summary>
    /// Gets the UTF-8 name of the panel. The overlay uses it as the window title and the menu label, and matches it without regard to ASCII case.
    /// </summary>
    ReadOnlySpan<byte> Name { get; }

    /// <summary>
    /// Submits the ImGui widgets of the panel inside its window, once for each frame. The overlay calls it on the compositor thread.
    /// </summary>
    /// <param name="draw">The frame size, the clock, the text buffer and the overlay. It is valid only during the call.</param>
    void Draw(ref InspectorDraw draw);
}
