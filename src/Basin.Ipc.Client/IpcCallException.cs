namespace Basin.Ipc;

public sealed class IpcCallException : Exception
{
    public IpcCallException()
    {
        Code = IpcErrorCodes.Internal;
        Method = string.Empty;
    }

    public IpcCallException(string message)
        : base(message)
    {
        Code = IpcErrorCodes.Internal;
        Method = string.Empty;
    }

    public IpcCallException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = IpcErrorCodes.Internal;
        Method = string.Empty;
    }

    public IpcCallException(string method, string code, string message)
        : base($"{method}: {code}: {message}")
    {
        Method = method;
        Code = code;
        Reason = message;
    }

    public string Method { get; }

    public string Reason { get; } = string.Empty;

    public string Code { get; }
}
