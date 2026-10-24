namespace Basin.Shell.Nested;

/// <summary>
/// One mapped window as <see cref="NestedShell.SnapshotWindows"/> reports it for a panel.
/// </summary>
/// <param name="Id">The shell-local window id, as <see cref="ManagedWindow.Id"/>.</param>
/// <param name="Title">The title, or the app id when the title is empty.</param>
/// <param name="AppId">The app id that the client set.</param>
/// <param name="Suffix">The title suffix from <see cref="NestedShell.WindowSuffix"/>, or <see langword="null"/>.</param>
/// <param name="Workspace">The zero-based workspace index.</param>
/// <param name="Sticky">A value indicating whether the window shows on every workspace.</param>
/// <param name="Focused">A value indicating whether the window has focus.</param>
/// <param name="Minimized">A value indicating whether the window is minimized.</param>
/// <param name="DemandsAttention">A value indicating whether the window mapped without focus and was not activated since.</param>
/// <param name="Frame">The frame box in logical layout coordinates.</param>
/// <param name="Icon">The icon as an absolute file path, or <see langword="null"/> when the icon is not a rooted path.</param>
public sealed record PanelWindowInfo(
    long Id,
    string Title,
    string AppId,
    string? Suffix,
    int Workspace,
    bool Sticky,
    bool Focused,
    bool Minimized,
    bool DemandsAttention,
    Box Frame,
    string? Icon = null);
