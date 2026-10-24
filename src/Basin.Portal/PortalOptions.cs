namespace Basin.Portal;

/// <summary>
/// Settings for the portal modules. Register an instance in <see cref="BasinServices"/> to change the defaults.
/// </summary>
public sealed class PortalOptions
{
    /// <summary>
    /// Gets or sets the directory for screenshot files. <see langword="null"/> or empty uses <c>$XDG_CACHE_HOME/basin/screenshots</c>.
    /// </summary>
    public string? ScreenshotDirectory { get; set; }

    /// <summary>
    /// Gets or sets the vendor name in the restore data. Restore data from another vendor is ignored. The default is <c>basin</c>.
    /// </summary>
    public string RestoreVendor { get; set; } = "basin";

    /// <summary>
    /// Gets or sets a value indicating whether a shortcut gets the trigger that the application prefers with no prompt. The default is <see langword="true"/>.
    /// </summary>
    public bool AcceptPreferredTriggers { get; set; } = true;

    /// <summary>
    /// Gets the directory for screenshot files. The call does not create it.
    /// </summary>
    /// <returns><see cref="ScreenshotDirectory"/>, or the default under the user cache directory.</returns>
    public string ResolveScreenshotDirectory()
    {
        if (!string.IsNullOrEmpty(ScreenshotDirectory))
        {
            return ScreenshotDirectory;
        }

        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrEmpty(cache))
        {
            cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        }

        return Path.Combine(cache, "basin", "screenshots");
    }
}
