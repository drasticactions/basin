namespace Basin.Capabilities;

/// <summary>
/// Finds the display name and the icon of an application from its app id.
/// </summary>
public interface IAppInfoResolver
{
    /// <summary>
    /// Looks up the name and the icon for an app id.
    /// </summary>
    /// <param name="appId">The app id that the client set, for example on its toplevel.</param>
    /// <param name="info">The name and icon path when the lookup succeeds, otherwise the default value.</param>
    /// <returns><see langword="true"/> when a name or an icon was found, otherwise <see langword="false"/>.</returns>
    bool TryResolve(string appId, out AppInfo info);
}
