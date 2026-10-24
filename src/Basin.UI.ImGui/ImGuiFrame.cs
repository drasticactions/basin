namespace Basin.UI.ImGui;

/// <summary>
/// The state of one frame that an <see cref="ImGuiUISurface"/> gives to <see cref="IImGuiContent.Build"/>. It lives only for the call.
/// </summary>
public readonly ref struct ImGuiFrame
{
    private readonly ImGuiUISurface _surface;

    internal ImGuiFrame(ImGuiUISurface surface, int width, int height, double scale, float deltaTime)
    {
        _surface = surface;
        Width = width;
        Height = height;
        Scale = scale;
        DeltaTime = deltaTime;
    }

    /// <summary>
    /// Gets the width of the surface in logical pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the surface in logical pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the scale from logical to buffer pixels. ImGui rasterizes the glyphs at this scale.
    /// </summary>
    public double Scale { get; }

    /// <summary>
    /// Gets the time since the last frame in seconds. It is never 0.
    /// </summary>
    public float DeltaTime { get; }

    /// <summary>
    /// Gets the surface that draws the frame.
    /// </summary>
    public ImGuiUISurface Surface => _surface;

    /// <summary>
    /// Keeps the surface drawing at a rate after this frame. The request lasts for one frame, so a live panel calls it in each frame. The highest rate of the frame wins.
    /// </summary>
    /// <param name="hz">The frames per second. A value of 0 or less has no effect.</param>
    public void RequestRefresh(int hz) => _surface.RequestRefresh(hz);
}
