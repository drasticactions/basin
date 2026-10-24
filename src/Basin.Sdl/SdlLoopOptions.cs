namespace Basin.Sdl;

/// <summary>
/// The settings that <see cref="SdlLoop"/> applies as hints before it initializes SDL.
/// </summary>
public sealed record SdlLoopOptions
{
    /// <summary>
    /// Gets the application name that SDL gives to the window system. It is <see langword="null"/> to keep the SDL default.
    /// </summary>
    public string? AppName { get; init; }

    /// <summary>
    /// Gets the SDL video driver to use, for example "wayland" or "offscreen". It is <see langword="null"/> or empty to let SDL choose.
    /// </summary>
    public string? VideoDriver { get; init; }
}
