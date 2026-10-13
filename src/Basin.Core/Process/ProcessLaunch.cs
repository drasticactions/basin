namespace Basin;

public sealed record ProcessLaunch(IReadOnlyList<string> Argv)
{
    public IReadOnlyDictionary<string, string>? Env { get; init; }

    public IReadOnlyList<string>? UnsetEnv { get; init; }

    public string? Cwd { get; init; }

    public string? LogPath { get; init; }

    public X11Density? X11 { get; init; }

    public string? DesktopId { get; init; }
}
