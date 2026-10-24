namespace Basin;

/// <summary>
/// Describes a child process that <see cref="ProcessTracker.Spawn"/> starts.
/// </summary>
/// <param name="Argv">The program and its arguments. The first item must not be empty, and the program is found through <c>PATH</c>.</param>
public sealed record ProcessLaunch(IReadOnlyList<string> Argv)
{
    /// <summary>
    /// Gets the variables to set in the child. They override the base environment and the X11 route.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Env { get; init; }

    /// <summary>
    /// Gets the variables to remove from the child environment. The removal happens last.
    /// </summary>
    public IReadOnlyList<string>? UnsetEnv { get; init; }

    /// <summary>
    /// Gets the absolute working directory of the child, or <see langword="null"/> to keep the directory of the compositor.
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// Gets the absolute path of a file that receives standard output and standard error, or <see langword="null"/> to keep the streams of the compositor.
    /// </summary>
    public string? LogPath { get; init; }

    /// <summary>
    /// Gets the Xwayland server to route the child to, or <see langword="null"/> to use <see cref="ProcessTracker.X11Default"/>.
    /// </summary>
    public X11Density? X11 { get; init; }

    /// <summary>
    /// Gets the desktop entry ID that the launch came from, or <see langword="null"/>. Launch rules can match it.
    /// </summary>
    public string? DesktopId { get; init; }
}
