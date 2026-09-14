using Basin.Capabilities;
using Basin.Freedesktop;

namespace BasinPortal;

public sealed class DesktopAppInfoResolver : IAppInfoResolver
{
    private readonly DesktopEntries _entries;
    private readonly IconSearch _icons;

    public DesktopAppInfoResolver(DesktopEntries entries, IconSearch icons)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(icons);
        _entries = entries;
        _icons = icons;
        _icons.Entries = entries;
    }

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
