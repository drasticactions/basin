namespace Basin.Ipc;

public sealed record IpcLaunch(IReadOnlyList<string> Argv)
{
    public IReadOnlyDictionary<string, string>? Env { get; init; }

    public IReadOnlyList<string>? UnsetEnv { get; init; }

    public string? Cwd { get; init; }

    public string? LogPath { get; init; }
}
