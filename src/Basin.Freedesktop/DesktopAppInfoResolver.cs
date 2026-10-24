using Basin.Capabilities;

namespace Basin.Freedesktop;

/// <summary>
/// An <see cref="IAppInfoResolver"/> that turns a Wayland <c>app_id</c> into a name and an icon from desktop entries and the icon theme.
/// </summary>
public sealed class DesktopAppInfoResolver : IAppInfoResolver
{
    private readonly DesktopEntries _entries;
    private readonly IconSearch _icons;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopAppInfoResolver"/> class.
    /// It sets <see cref="IconSearch.Entries"/> on <paramref name="icons"/> to <paramref name="entries"/>, so both lookups use the same set.
    /// </summary>
    /// <param name="entries">The desktop entries that supply the name.</param>
    /// <param name="icons">The icon search that supplies the icon path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> or <paramref name="icons"/> is <see langword="null"/>.</exception>
    public DesktopAppInfoResolver(DesktopEntries entries, IconSearch icons)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(icons);
        _entries = entries;
        _icons = icons;
        _icons.Entries = entries;
    }

    /// <summary>
    /// Looks up the name through <see cref="DesktopEntries.FindForAppId(string)"/> and the icon through <see cref="IconSearch.Find(string)"/>.
    /// A part that is not found is an empty string.
    /// </summary>
    /// <param name="appId">The Wayland <c>app_id</c> to resolve.</param>
    /// <param name="info">The name and icon path when the method returns <see langword="true"/>, otherwise the default value.</param>
    /// <returns><see langword="false"/> when <paramref name="appId"/> is null or empty, or when neither a name nor an icon is found.</returns>
    public bool TryResolve(string appId, out AppInfo info)
    {
        info = default;
        if (string.IsNullOrEmpty(appId))
        {
            return false;
        }

        var name = _entries.FindForAppId(appId)?.Name ?? "";
        var icon = _icons.Find(appId) ?? "";
        if (name.Length == 0 && icon.Length == 0)
        {
            return false;
        }

        info = new AppInfo(name, icon);
        return true;
    }
}
