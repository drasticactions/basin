namespace Basin;

/// <summary>
/// POSIX signal numbers for <see cref="ICompositorEventLoop.AddSignal"/>.
/// </summary>
public static class Signal
{
    /// <summary>
    /// SIGHUP.
    /// </summary>
    public const int Hangup = 1;

    /// <summary>
    /// SIGINT.
    /// </summary>
    public const int Interrupt = 2;

    /// <summary>
    /// SIGTERM.
    /// </summary>
    public const int Terminate = 15;
}
