using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Tmds.DBus.Protocol;

namespace Basin.Plasma;

public sealed class OrientationSensor : IOrientationSource, IDisposable
{
    private const string Service = "net.hadess.SensorProxy";
    private const string Path = "/net/hadess/SensorProxy";
    private const string SensorInterface = "net.hadess.SensorProxy";

    private readonly ICompositorEventLoop _loop;
    private readonly int[] _pipe = [-1, -1];
    private readonly IEventSource? _wake;
    private readonly object _lock = new();
    private DBusConnection? _connection;
    private bool _pendingAvailable;
    private string? _pendingOrientation;
    private bool _wantEnabled;
    private bool _claimed;
    private volatile bool _disposed;

    public OrientationSensor(ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
        unsafe
        {
            fixed (int* fds = _pipe)
            {
                if (pipe2(fds, OCloexec) != 0)
                {
                    throw new InvalidOperationException("the sensor wake pipe could not be created");
                }
            }
        }

        _wake = loop.AddFd(_pipe[0], FdReadiness.Readable, OnWake);
        _ = BindAsync();
    }

    public bool IsAvailable { get; private set; }

    public OutputTransform? Orientation { get; private set; }

    public event Action? Changed;

    public void SetEnabled(bool enabled)
    {
        DBusConnection? connection;
        lock (_lock)
        {
            if (_wantEnabled == enabled)
            {
                return;
            }

            _wantEnabled = enabled;
            connection = _connection;
        }

        if (connection is not null)
        {
            _ = SyncClaimAsync(connection);
        }
    }

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
            if (DBusAddress.System is not { } address)
            {
                BasinLog.Debug($"this session has no system bus, auto rotate is off");
                return;
            }

            var connection = new DBusConnection(address);
            await connection.ConnectAsync();
            lock (_lock)
            {
                if (_disposed)
                {
                    connection.Dispose();
                    return;
                }

                _connection = connection;
            }

            _ = await connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = "org.freedesktop.DBus.Properties",
                    Member = "PropertiesChanged",
                    Path = Path,
                },
                ReadPropertiesChanged,
                OnPropertiesChanged,
                emitOnCapturedContext: false,
                ObserverFlags.None,
                state: this);

            var available = await GetBoolAsync(connection, "HasAccelerometer");
            Publish(available, null);
            await SyncClaimAsync(connection);
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            if (!_disposed)
            {
                BasinLog.Debug($"iio-sensor-proxy is not reachable, auto rotate is off ({error.Message})");
            }
        }
    }

    private async Task SyncClaimAsync(DBusConnection connection)
    {
        try
        {
            bool claim;
            lock (_lock)
            {
                if (_claimed == _wantEnabled)
                {
                    return;
                }

                claim = _wantEnabled;
                _claimed = claim;
            }

            try
            {
                await connection.CallMethodAsync(ClaimMessage(connection, claim));
            }
            catch
            {
                lock (_lock)
                {
                    _claimed = !claim;
                }

                throw;
            }

            if (claim)
            {
                var orientation = await GetStringAsync(connection, "AccelerometerOrientation");
                Publish(null, orientation);
            }
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            if (!_disposed)
            {
                BasinLog.Warn($"the accelerometer claim failed ({error.Message})");
            }
        }
    }

    private static MessageBuffer ClaimMessage(DBusConnection connection, bool claim)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            Service, Path, SensorInterface, claim ? "ClaimAccelerometer" : "ReleaseAccelerometer");
        return writer.CreateMessage();
    }

    private static Task<VariantValue> GetAsync(DBusConnection connection, string property)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, Path, "org.freedesktop.DBus.Properties", "Get", "ss");
        writer.WriteString(SensorInterface);
        writer.WriteString(property);
        return connection.CallMethodAsync(
            writer.CreateMessage(), static (Message message, object? _) => message.GetBodyReader().ReadVariantValue(), null);
    }

    private static async Task<bool> GetBoolAsync(DBusConnection connection, string property) =>
        (await GetAsync(connection, property)).GetBool();

    private static async Task<string> GetStringAsync(DBusConnection connection, string property) =>
        (await GetAsync(connection, property)).GetString();

    private static (string Interface, Dictionary<string, VariantValue> Changed) ReadPropertiesChanged(
        Message message, object? state)
    {
        var reader = message.GetBodyReader();
        var name = reader.ReadString();
        var changed = reader.ReadDictionaryOfStringToVariantValue();
        return (name, changed);
    }

    private static void OnPropertiesChanged(
        Notification<(string Interface, Dictionary<string, VariantValue> Changed)> notification)
    {
        if (!notification.HasValue || notification.State is not OrientationSensor sensor ||
            notification.Value.Interface != SensorInterface)
        {
            return;
        }

        bool? available = null;
        string? orientation = null;
        if (notification.Value.Changed.TryGetValue("HasAccelerometer", out var has))
        {
            available = has.GetBool();
        }

        if (notification.Value.Changed.TryGetValue("AccelerometerOrientation", out var reading))
        {
            orientation = reading.GetString();
        }

        if (available is not null || orientation is not null)
        {
            sensor.Publish(available, orientation);
        }
    }

    private void Publish(bool? available, string? orientation)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (available is { } has)
            {
                _pendingAvailable = has;
            }

            if (orientation is not null)
            {
                _pendingOrientation = orientation;
            }
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

        bool available;
        string? orientation;
        lock (_lock)
        {
            available = _pendingAvailable;
            orientation = _pendingOrientation;
        }

        var mapped = Map(orientation);
        if (available == IsAvailable && mapped == Orientation)
        {
            return;
        }

        IsAvailable = available;
        Orientation = mapped;
        Changed?.Invoke();
    }

    private static OutputTransform? Map(string? orientation) => orientation switch
    {
        "normal" => OutputTransform.Normal,
        "bottom-up" => OutputTransform.Rotate180,
        "left-up" => OutputTransform.Rotate90,
        "right-up" => OutputTransform.Rotate270,
        _ => null,
    };

    private const int OCloexec = 0x80000;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
