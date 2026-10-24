namespace Basin.Config;

/// <summary>
/// The facts about one window that <see cref="WindowRule"/> matches. A <see langword="null"/> fact is unknown.
/// </summary>
public readonly record struct WindowFacts
{
    /// <summary>
    /// Gets the xdg app ID, or the class half of <c>WM_CLASS</c> for an X11 window.
    /// </summary>
    public string? AppId { get; init; }

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the class half of <c>WM_CLASS</c>. It is <see langword="null"/> for a Wayland window.
    /// </summary>
    public string? X11Class { get; init; }

    /// <summary>
    /// Gets the instance half of <c>WM_CLASS</c>. It is <see langword="null"/> for a Wayland window.
    /// </summary>
    public string? X11Instance { get; init; }

    /// <summary>
    /// Gets the Steam game ID from the window property or from the process environment.
    /// </summary>
    public uint? SteamGame { get; init; }

    /// <summary>
    /// Gets the process name from <c>/proc/PID/comm</c>.
    /// </summary>
    public string? ProcessName { get; init; }

    /// <summary>
    /// Gets the base name of the executable of the process.
    /// </summary>
    public string? Executable { get; init; }

    /// <summary>
    /// Gets the window type in lower case, for example <c>normal</c> or <c>dialog</c>.
    /// </summary>
    public string? WindowType { get; init; }

    /// <summary>
    /// Gets a value indicating whether an X11 window wants keyboard focus.
    /// </summary>
    public bool? WantsFocus { get; init; }

    /// <summary>
    /// Gets a value indicating whether an X11 window accepts input by its <c>WM_HINTS</c>. A window with no input hint accepts input.
    /// </summary>
    public bool? AcceptsInput { get; init; }

    /// <summary>
    /// Gets a value indicating whether an X11 window supports <c>WM_TAKE_FOCUS</c>.
    /// </summary>
    public bool? TakesFocus { get; init; }

    /// <summary>
    /// Gets a value indicating whether an X11 window asks to be left out of the taskbar.
    /// </summary>
    public bool? SkipTaskbar { get; init; }

    /// <summary>
    /// Gets a value indicating whether an X11 window asks to be left out of the pager.
    /// </summary>
    public bool? SkipPager { get; init; }

    /// <summary>
    /// Gets the Win32 window style that Wine reports for an X11 window.
    /// </summary>
    public uint? HwndStyle { get; init; }

    /// <summary>
    /// Gets the Win32 extended window style that Wine reports for an X11 window.
    /// </summary>
    public uint? HwndStyleEx { get; init; }

    /// <summary>
    /// Gets a value indicating whether an X11 window is mapped at a size of 1x1 or less, which marks a helper window.
    /// </summary>
    public bool? IsUseless { get; init; }

    /// <summary>
    /// Builds the facts of an xdg toplevel.
    /// </summary>
    /// <param name="appId">The app ID.</param>
    /// <param name="title">The title.</param>
    /// <param name="dialog"><see langword="true"/> when the toplevel has a parent. The window type is then <c>dialog</c>, else <c>normal</c>.</param>
    /// <param name="process">The process of the client.</param>
    /// <returns>The facts.</returns>
    public static WindowFacts ForWayland(string? appId, string? title, bool dialog, in ProcessIdentity process) =>
        new WindowFacts { AppId = appId, Title = title, WindowType = dialog ? "dialog" : "normal" }.WithProcess(process);

    /// <summary>
    /// Builds the facts of an X11 window. The app ID is the class.
    /// </summary>
    /// <param name="x11Class">The class half of <c>WM_CLASS</c>.</param>
    /// <param name="x11Instance">The instance half of <c>WM_CLASS</c>.</param>
    /// <param name="title">The title.</param>
    /// <param name="steamGame">The <c>STEAM_GAME</c> property, or <see langword="null"/>.</param>
    /// <param name="windowType">The window type in lower case.</param>
    /// <param name="process">The process of the window.</param>
    /// <returns>The facts.</returns>
    public static WindowFacts ForX11(
        string x11Class, string x11Instance, string title, uint? steamGame, string windowType, in ProcessIdentity process) =>
        new WindowFacts
        {
            AppId = x11Class,
            Title = title,
            X11Class = x11Class,
            X11Instance = x11Instance,
            SteamGame = steamGame,
            WindowType = windowType,
        }.WithProcess(process);

    /// <summary>
    /// Adds the process facts. A Steam game ID that is already set wins over the one of the process. An empty process name or executable keeps the old value.
    /// </summary>
    /// <param name="process">The process identity.</param>
    /// <returns>A copy with the process facts.</returns>
    public WindowFacts WithProcess(in ProcessIdentity process) => this with
    {
        SteamGame = SteamGame ?? process.SteamGame,
        ProcessName = process.Name is { Length: > 0 } name ? name : ProcessName,
        Executable = process.Executable is { Length: > 0 } executable ? executable : Executable,
    };
}
