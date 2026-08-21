using Seatd;

namespace Basin.Session;

public sealed class DirectSession : ISession
{
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    public string SeatName => "seat0";

    public bool IsActive => true;

    public event Action? Enabled
    {
        add { }
        remove { }
    }

    public event Action? Disabled
    {
        add { }
        remove { }
    }

    public ISessionDevice OpenDevice(string path)
    {
        var fd = open(path, 0x80002 );
        if (fd < 0)
        {
            throw new InvalidOperationException(
                $"open {path} failed (errno {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}). " +
                "Direct device access needs membership in the video/input groups; " +
                "prefer a seat manager (seatd/logind).");
        }

        return new DirectDevice(fd, path);
    }

    public void SwitchSession(int session) => throw new NotSupportedException("no seat manager to switch sessions");

    public void Dispose()
    {
    }

    private sealed class DirectDevice(int fd, string path) : ISessionDevice
    {
        public int FileDescriptor => fd;

        public string Path => path;

        public void Dispose() => close(fd);
    }
}
