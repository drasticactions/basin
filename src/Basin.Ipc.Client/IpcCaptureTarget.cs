namespace Basin.Ipc;

/// <summary>
/// Where a capture goes: a PNG file that the compositor writes, a descriptor of raw pixels, or an inline PNG.
/// </summary>
public abstract record IpcCaptureTarget
{
    /// <summary>
    /// Gets the target that returns a memfd of raw pixels with no encoding. It is not scaled.
    /// </summary>
    public static IpcCaptureTarget Fd { get; } = new FdTarget();

    /// <summary>
    /// Gets the target that returns the PNG in the reply. A PNG that does not fit in a frame answers <c>too_large</c>.
    /// </summary>
    public static IpcCaptureTarget Inline { get; } = new InlineTarget();

    /// <summary>
    /// Makes a target that has the compositor write a PNG file.
    /// </summary>
    /// <param name="path">The file path. A relative path is made absolute against the caller's working directory.</param>
    /// <returns>The target.</returns>
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
