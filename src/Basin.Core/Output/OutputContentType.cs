namespace Basin;

/// <summary>
/// Names the content type that a connector sends to the display, so the display can pick a picture mode. The values match the DRM <c>content type</c> property.
/// </summary>
public enum OutputContentType
{
    /// <summary>
    /// No content type. The compositor sends it while it composites several windows.
    /// </summary>
    None = 0,
    /// <summary>
    /// Desktop graphics or text.
    /// </summary>
    Graphics = 1,
    /// <summary>
    /// Still photographs.
    /// </summary>
    Photo = 2,
    /// <summary>
    /// Film or video.
    /// </summary>
    Cinema = 3,
    /// <summary>
    /// A game that needs low latency.
    /// </summary>
    Game = 4,
}
