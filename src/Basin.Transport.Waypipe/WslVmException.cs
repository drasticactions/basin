namespace Basin.Transport.Waypipe;

/// <summary>
/// The error for a WSL2 VM id that cannot be resolved, for example because WSL is too old or the VM does not run.
/// </summary>
public sealed class WslVmException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WslVmException"/> class.
    /// </summary>
    public WslVmException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WslVmException"/> class with a message.
    /// </summary>
    /// <param name="message">The description of the failure.</param>
    public WslVmException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WslVmException"/> class with the error that caused it.
    /// </summary>
    /// <param name="message">The description of the failure.</param>
    /// <param name="inner">The error that caused this error.</param>
    public WslVmException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
