namespace Basin.WindowManager;

/// <summary>
/// How a frame reaches the screen: at the vertical blank, or as soon as it is ready.
/// </summary>
public enum PresentationMode
{
    /// <summary>The compositor presents frames at the vertical blank, with no tearing. This is the default.</summary>
    Vsync = 0,

    /// <summary>The compositor presents frames as soon as they are ready, which can tear.</summary>
    Async = 1,
}
