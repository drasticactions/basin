namespace Basin.Avalonia;

internal static class ChannelSelection
{
    internal static void Write(ClientFd fd, byte[] bytes)
    {
        if (Resolve(fd) is { CanWrite: true } pipe)
        {
            pipe.Write(bytes);
            pipe.CloseWrite();
        }

        fd.Close();
    }

    internal static IPipeToClient? Resolve(ClientFd fd)
    {
        if (fd.Owner?.FdSlots is not { } slots || fd.Value < 0)
        {
            return null;
        }

        try
        {
            return slots.Resolve<object>(fd.Value) as IPipeToClient;
        }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException or KeyNotFoundException or ObjectDisposedException)
        {
            return null;
        }
    }
}
