namespace Basin.Capabilities;

/// <summary>
/// The name and icon that an <see cref="IAppInfoResolver"/> found for an application id.
/// </summary>
/// <param name="DisplayName">The name from the desktop entry, or an empty string when no entry names the application.</param>
/// <param name="IconPath">The path of an icon file, or an empty string when no icon was found.</param>
public readonly record struct AppInfo(string DisplayName, string IconPath);
