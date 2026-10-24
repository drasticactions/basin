namespace Basin.Backend.Drm;

/// <summary>
/// What <see cref="DrmBackend.Dispose"/> leaves on the screen.
/// </summary>
public enum DrmExitMode
{
    /// <summary>
    /// Restore the CRTC state that was present before <see cref="DrmBackend.Start"/>, for example the console.
    /// </summary>
    Restore,
    /// <summary>
    /// Leave the last frame of each output on its CRTC, so that the next compositor can inherit it.
    /// </summary>
    Retain,
}
