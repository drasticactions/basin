namespace Basin.Capabilities.Defaults;

public sealed class CursorImageTheme : ICursorTheme
{
    public CursorImageTheme(CursorImages? images = null) => Images = images;

    public CursorImages? Images { get; set; }

    public bool TryResolve(string shapeName, double scale, out CursorImage image)
    {
        if (Images?.Named(shapeName, new CursorKey(scale, null)) is { } found)
        {
            image = found;
            return true;
        }

        image = default;
        return false;
    }
}
