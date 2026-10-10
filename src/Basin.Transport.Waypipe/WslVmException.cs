namespace Basin.Transport.Waypipe;

public sealed class WslVmException : Exception
{
    public WslVmException()
    {
    }

    public WslVmException(string message)
        : base(message)
    {
    }

    public WslVmException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
