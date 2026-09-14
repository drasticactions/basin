namespace Basin.Portal;

public sealed class PortalOptions
{
    public string? ScreenshotDirectory { get; set; }

    public string RestoreVendor { get; set; } = "basin";

    public bool AcceptPreferredTriggers { get; set; } = true;

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
