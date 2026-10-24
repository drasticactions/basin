namespace Basin.Cli;

/// <summary>
/// Holds the parsed value of the <c>--backend</c> option.
/// </summary>
/// <param name="Kind">The backend that the outputs go to.</param>
/// <param name="SocketFd">The inherited listening socket that the <c>KIND:FD</c> form names, or -1 when the option names none.</param>
public readonly record struct BackendChoice(BackendKind Kind, int SocketFd = -1)
{
    /// <summary>
    /// Formats the choice in the spelling that <c>--backend</c> accepts.
    /// </summary>
    /// <returns>The backend name, for example <c>drm</c>, followed by <c>:FD</c> when <see cref="SocketFd"/> is zero or more.</returns>
    public override string ToString() =>
        SocketFd >= 0 ? $"{Name}:{SocketFd}" : Name;

    private string Name => Kind switch
    {
        BackendKind.Drm => "drm",
        BackendKind.Headless => "headless",
        _ => "nested",
    };
}
