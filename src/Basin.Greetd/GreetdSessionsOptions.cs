using Basin.Freedesktop;

namespace Basin.Greetd;

/// <summary>
/// The options for <see cref="GreetdSessions.List"/>.
/// </summary>
public sealed class GreetdSessionsOptions
{
    /// <summary>
    /// Gets the data directories to search for session entries. <see langword="null"/> uses the XDG data directories.
    /// </summary>
    public IReadOnlyList<string>? DataDirectories { get; init; }

    /// <summary>
    /// Gets the arguments that go before an X11 session command. An empty list offers no X11 session. The default is <c>startx</c>.
    /// </summary>
    public IReadOnlyList<string> X11Wrapper { get; init; } = ["startx"];

    /// <summary>
    /// Gets the path of the environments file, with one command for each line. <see langword="null"/> or an empty string reads no file.
    /// </summary>
    public string? EnvironmentsPath { get; init; } = "/etc/greetd/environments";

    /// <summary>
    /// Gets the locale for the session names. <see langword="null"/> uses the locale of the process environment.
    /// </summary>
    public DesktopLocale? Locale { get; init; }

    /// <summary>
    /// Gets the test for a <c>TryExec</c> value. It returns <see langword="true"/> when the program exists. <see langword="null"/> uses the default search.
    /// </summary>
    public Func<string, bool>? TryExec { get; init; }
}
