namespace Basin.Ipc;

public abstract record IpcCaptureTarget
{
    public static IpcCaptureTarget Fd { get; } = new FdTarget();

    public static IpcCaptureTarget Inline { get; } = new InlineTarget();

    public static IpcCaptureTarget ToPath(string path) => new PathTarget(Path.GetFullPath(path));

    internal abstract IpcCaptureTo Wire { get; }

    private sealed record FdTarget : IpcCaptureTarget
    {
        internal override IpcCaptureTo Wire => IpcCaptureTo.Fd;
    }

    private sealed record InlineTarget : IpcCaptureTarget
    {
        internal override IpcCaptureTo Wire => IpcCaptureTo.Inline;
    }

    private sealed record PathTarget(string FilePath) : IpcCaptureTarget
    {
        internal override IpcCaptureTo Wire => IpcCaptureTo.ToPath(FilePath);
    }
}
