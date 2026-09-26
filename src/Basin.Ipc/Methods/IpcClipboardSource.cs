namespace Basin.Ipc;

internal static class IpcClipboardSource
{
    public static readonly string[] Types = ["text/plain;charset=utf-8", "text/plain", "UTF8_STRING"];

    public static DataSource Create(IpcServer server, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return new DataSource([.. Types], (_, fd) => Send(server, bytes, fd));
    }

    private static void Send(IpcServer server, byte[] bytes, ClientFd fd)
    {
        if (fd.Owner?.FdSlots is { } slots)
        {
            try
            {
                if (slots.Resolve<object>(fd.Value) is IPipeToClient { CanWrite: true } pipe)
                {
                    pipe.Write(bytes);
                    pipe.CloseWrite();
                }
            }
            catch (Exception error) when (
                error is ArgumentException or InvalidOperationException or KeyNotFoundException or ObjectDisposedException)
            {
            }

            fd.Close();
            return;
        }

        if (fd.Value < 0)
        {
            return;
        }

        new IpcFdWriter(server, fd, bytes).Start();
    }
}
