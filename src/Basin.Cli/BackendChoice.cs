namespace Basin.Cli;

public readonly record struct BackendChoice(BackendKind Kind, int SocketFd = -1)
{
    public override string ToString() =>
        SocketFd >= 0 ? $"{Name}:{SocketFd}" : Name;

    private string Name => Kind switch
    {
        BackendKind.Drm => "drm",
        BackendKind.Headless => "headless",
        _ => "nested",
    };
}
