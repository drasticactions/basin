using Basin.Freedesktop;

namespace Basin.Greetd;

public sealed class GreetdSessionsOptions
{
    public IReadOnlyList<string>? DataDirectories { get; init; }

    public IReadOnlyList<string> X11Wrapper { get; init; } = ["startx"];

    public string? EnvironmentsPath { get; init; } = "/etc/greetd/environments";

    public DesktopLocale? Locale { get; init; }

    public Func<string, bool>? TryExec { get; init; }
}
