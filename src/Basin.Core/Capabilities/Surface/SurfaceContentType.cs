namespace Basin.Capabilities;

/// <summary>
/// Lists the content types that a client can declare for a surface.
/// </summary>
public enum SurfaceContentType
{
    /// <summary>
    /// The client declared no type.
    /// </summary>
    None = 0,
    /// <summary>
    /// Still images.
    /// </summary>
    Photo = 1,
    /// <summary>
    /// Video playback.
    /// </summary>
    Video = 2,
    /// <summary>
    /// A game or other interactive content that needs low latency.
    /// </summary>
    Game = 3,
}
