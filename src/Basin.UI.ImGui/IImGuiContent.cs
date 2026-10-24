namespace Basin.UI.ImGui;

/// <summary>
/// The panels that an <see cref="ImGuiUISurface"/> draws. The surface calls it once for each frame, inside its ImGui context.
/// </summary>
public interface IImGuiContent
{
    /// <summary>
    /// Submits the ImGui windows and widgets of one frame. An exception goes to <see cref="ImGuiUISurface.Faulted"/>, and the frame still renders.
    /// </summary>
    /// <param name="frame">The logical size, the scale and the time step of the frame. It is valid only during the call.</param>
    void Build(ImGuiFrame frame);
}
