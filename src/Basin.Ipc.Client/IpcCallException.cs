namespace Basin.Ipc;

/// <summary>
/// The compositor answered a call with an error.
/// </summary>
public sealed class IpcCallException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpcCallException"/> class with the <c>internal</c> code.
    /// </summary>
    public IpcCallException()
    {
        Code = IpcErrorCodes.Internal;
        Method = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcCallException"/> class with the <c>internal</c> code.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public IpcCallException(string message)
        : base(message)
    {
        Code = IpcErrorCodes.Internal;
        Method = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcCallException"/> class with the <c>internal</c> code.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public IpcCallException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = IpcErrorCodes.Internal;
        Method = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcCallException"/> class from an error reply. The message is <c>method: code: message</c>.
    /// </summary>
    /// <param name="method">The called method.</param>
    /// <param name="code">The error code, for example one of <see cref="IpcErrorCodes"/>.</param>
    /// <param name="message">The error text from the compositor.</param>
    public IpcCallException(string method, string code, string message)
        : base($"{method}: {code}: {message}")
    {
        Method = method;
        Code = code;
        Reason = message;
    }

    /// <summary>
    /// Gets the called method. It is empty when the exception does not come from a reply.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// Gets the error text from the compositor, without the method and the code. It is empty when the exception does not come from a reply.
    /// </summary>
    public string Reason { get; } = string.Empty;

    /// <summary>
    /// Gets the error code, for example <see cref="IpcErrorCodes.NotFound"/>.
    /// </summary>
    public string Code { get; }
}
