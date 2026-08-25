using System.Runtime.InteropServices;
using Basin;
using Basin.Capabilities;
using Tmds.DBus.Protocol;
using static PlasmaHost.PlasmaHostLog;

namespace PlasmaHost;

internal sealed class KdeConfigNotify : IDisposable
{
    private const string NotifyInterface = "org.kde.kconfig.notify";
    private const string NotifyMember = "ConfigChanged";
    private const string GlobalsPath = "/kdeglobals";
    private const int OCloexec = 0x80000;

    private readonly int[] _pipe = [-1, -1];
    private readonly IEventSource? _wake;
    private DBusConnection? _connection;
    private volatile bool _disposed;

    public KdeConfigNotify(ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        unsafe
        {
            fixed (int* fds = _pipe)
            {
                if (pipe2(fds, OCloexec) != 0)
                {
                    throw new InvalidOperationException("the config notify wake pipe could not be created");
                }
            }
        }

        _wake = loop.AddFd(_pipe[0], FdReadiness.Readable, OnWake);
        _ = BindAsync();
    }

    public event Action? Changed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection?.Dispose();
        _connection = null;
        _wake?.Remove();
        for (var i = 0; i < _pipe.Length; i++)
        {
            if (_pipe[i] >= 0)
            {
                _ = close(_pipe[i]);
                _pipe[i] = -1;
            }
        }
    }

    private async Task BindAsync()
    {
        try
        {
            if (DBusAddress.Session is not { } address)
            {
                Log.Debug($"this session has no bus, the colour scheme is read once");
                return;
            }

            var connection = new DBusConnection(address);
            await connection.ConnectAsync();
            if (_disposed)
            {
                connection.Dispose();
                return;
            }

            _connection = connection;
            _ = await connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = NotifyInterface,
                    Member = NotifyMember,
                    Path = GlobalsPath,
                },
                static (Message message, object? _) => true,
                OnNotified,
                emitOnCapturedContext: false,
                ObserverFlags.None,
                state: this);
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            if (!_disposed)
            {
                Log.Debug($"kdeglobals notifications are not reachable ({error.Message})");
            }
        }
    }

    private static void OnNotified(Notification<bool> notification)
    {
        if (notification.HasValue && notification.State is KdeConfigNotify notify)
        {
            notify.Wake();
        }
    }

    private void Wake()
    {
        if (_disposed)
        {
            return;
        }

        unsafe
        {
            byte one = 1;
            _ = write(_pipe[1], &one, 1);
        }
    }

    private void OnWake(int fd, FdReadiness readiness)
    {
        unsafe
        {
            var drain = stackalloc byte[16];
            while (read(fd, drain, 16) == 16)
            {
            }
        }

        Changed?.Invoke();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
