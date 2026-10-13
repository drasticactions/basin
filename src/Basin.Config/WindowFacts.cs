namespace Basin.Config;

public readonly record struct WindowFacts
{
    public string? AppId { get; init; }

    public string? Title { get; init; }

    public string? X11Class { get; init; }

    public string? X11Instance { get; init; }

    public uint? SteamGame { get; init; }

    public string? ProcessName { get; init; }

    public string? Executable { get; init; }

    public string? WindowType { get; init; }

    public bool? WantsFocus { get; init; }

    public bool? AcceptsInput { get; init; }

    public bool? TakesFocus { get; init; }

    public bool? SkipTaskbar { get; init; }

    public bool? SkipPager { get; init; }

    public uint? HwndStyle { get; init; }

    public uint? HwndStyleEx { get; init; }

    public bool? IsUseless { get; init; }

    public static WindowFacts ForWayland(string? appId, string? title, bool dialog, in ProcessIdentity process) =>
        new WindowFacts { AppId = appId, Title = title, WindowType = dialog ? "dialog" : "normal" }.WithProcess(process);

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

    public WindowFacts WithProcess(in ProcessIdentity process) => this with
    {
        SteamGame = SteamGame ?? process.SteamGame,
        ProcessName = process.Name is { Length: > 0 } name ? name : ProcessName,
        Executable = process.Executable is { Length: > 0 } executable ? executable : Executable,
    };
}
