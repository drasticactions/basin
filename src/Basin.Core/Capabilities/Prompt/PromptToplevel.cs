namespace Basin.Capabilities;

/// <summary>
/// Describes one window that a source picker can offer.
/// </summary>
/// <param name="Id">The toplevel id from <see cref="IToplevelModel"/>.</param>
/// <param name="Title">The window title.</param>
/// <param name="AppId">The app id of the window.</param>
public readonly record struct PromptToplevel(ulong Id, string Title, string AppId);
