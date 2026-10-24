using Basin.Diagnostics;
using Tmds.DBus.Protocol;
using static Basin.SystemStatus.SystemStatusLog;

namespace Basin.SystemStatus;

/// <summary>
/// The base of the status readers. A reader reads the whole state of one D-Bus service again after each burst of signals, and it recovers when the service starts or restarts.
/// </summary>
public abstract class SystemStatusReader : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<IDisposable> _watches = [];
    private readonly string _indicator;
    private IEventSource? _timer;
    private string? _reason;
    private bool _armed;
    private bool _reading;
    private bool _dirty;
    private bool _logged;
    private bool _disposed;

    private protected SystemStatusReader(SystemStatusBus bus, string service, string indicator)
    {
        Bus = bus;
        Service = service;
        _indicator = indicator;
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the well-known bus name that the reader reads.
    /// </summary>
    public string Service { get; }

    /// <summary>
    /// Gets a value indicating whether the first read is complete, with or without a result.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the service answered the last read. A shell hides an indicator whose reader is not available.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Gets why the indicator is absent, for example that the service is not on the bus or that it reports no device. It is <see langword="null"/> when the indicator is present.
    /// </summary>
    public string? Reason => IsAvailable ? AvailableReason : _reason;

    /// <summary>
    /// Gets a value indicating whether the last read failed because the service is not on the bus. A shell can tell the user that the service does not run.
    /// </summary>
    public bool ServiceMissing { get; private set; }

    /// <summary>
    /// Occurs on the compositor thread after each read, successful or not. One burst of signals gives one event.
    /// </summary>
    public event Action? Changed;

    private protected SystemStatusBus Bus { get; }

    private protected DBusConnection? Connection { get; private set; }

    private protected virtual string? AvailableReason => null;

    /// <summary>
    /// Removes the signal watches and the timer. A result that arrives later is dropped. Call it on the compositor thread, before the bus is disposed. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        _disposed = true;
        foreach (var watch in _watches)
        {
            watch.Dispose();
        }

        _watches.Clear();
        _timer?.Remove();
        _timer = null;
        Connection = null;
        BasinCounters.Untrack();
    }

    private protected static bool CheckBus(SystemStatusBus bus, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(bus);
        if (bus.IsDisposed)
        {
            reason = "the system status bus is disposed";
            return false;
        }

        if (!bus.HasAddress)
        {
            reason = "this session has no system bus address";
            return false;
        }

        reason = null;
        return true;
    }

    private protected void Start() => _ = StartAsync();

    private protected abstract MatchRule[] Rules();

    private protected abstract Task<Action> ReadAsync(DBusConnection connection);

    private protected abstract void Clear();

    private protected Task<bool> CallAsync(MessageBuffer message, string what)
    {
        if (Connection is not { } connection || _disposed)
        {
            return Task.FromResult(false);
        }

        var completion = new TaskCompletionSource<bool>();
        _ = RunCallAsync(connection, message, what, completion);
        return completion.Task;
    }

    private protected MessageWriter Writer() => Connection!.GetMessageWriter();

    private protected void Refresh() => Schedule();

    private async Task RunCallAsync(DBusConnection connection, MessageBuffer message, string what, TaskCompletionSource<bool> completion)
    {
        var result = false;
        try
        {
            await connection.CallMethodAsync(message).ConfigureAwait(false);
            result = true;
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Warn(error, $"{Service}: {what} failed");
        }

        if (Bus.IsDisposed || _disposed)
        {
            completion.TrySetResult(false);
            return;
        }

        Bus.Post(() =>
        {
            completion.TrySetResult(result);
            if (result)
            {
                Schedule();
            }
        });
    }

    private async Task StartAsync()
    {
        var connection = await Bus.ConnectionAsync().ConfigureAwait(false);
        if (connection is null)
        {
            Bus.Post(() => Fail(Bus.Failure ?? "the system bus is not reachable"));
            return;
        }

        try
        {
            var rules = new List<MatchRule>(Rules())
            {
                new()
                {
                    Type = MessageType.Signal,
                    Sender = "org.freedesktop.DBus",
                    Path = "/org/freedesktop/DBus",
                    Interface = "org.freedesktop.DBus",
                    Member = "NameOwnerChanged",
                    Arg0 = Service,
                },
            };
            foreach (var rule in rules)
            {
                var watch = await connection.AddMatchAsync(
                    rule,
                    (Action<Notification>)(notification =>
                    {
                        if (!notification.IsCompletion)
                        {
                            Bus.Post(Schedule);
                        }
                    }),
                    false, ObserverFlags.None, null).ConfigureAwait(false);
                Bus.Post(() =>
                {
                    if (_disposed)
                    {
                        watch.Dispose();
                    }
                    else
                    {
                        _watches.Add(watch);
                    }
                });
            }

            Bus.Post(() =>
            {
                if (_disposed)
                {
                    return;
                }

                Connection = connection;
                Read();
            });
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Bus.Post(() => Fail($"the system bus refused a match for {Service} ({error.Message})"));
        }
    }

    private void Schedule()
    {
        if (_disposed || Connection is null)
        {
            return;
        }

        _timer ??= Bus.Loop.AddTimer(OnTimer);
        if (!_armed)
        {
            _armed = true;
            _timer.UpdateTimer(Math.Max(1, Bus.CoalesceMilliseconds));
        }
    }

    private void OnTimer()
    {
        _armed = false;
        Read();
    }

    private void Read()
    {
        if (_disposed || Connection is not { } connection)
        {
            return;
        }

        if (_reading)
        {
            _dirty = true;
            return;
        }

        _reading = true;
        _ = ReadAndApplyAsync(connection);
    }

    private async Task ReadAndApplyAsync(DBusConnection connection)
    {
        Action? apply = null;
        string? failure = null;
        var missing = false;
        try
        {
            apply = await ReadAsync(connection).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException error) when (error.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner")
        {
            failure = $"{Service} is not on the system bus";
            missing = true;
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            failure = $"{Service} did not answer as expected ({error.Message})";
        }

        Bus.Post(() =>
        {
            _reading = false;
            if (_disposed)
            {
                return;
            }

            if (apply is not null)
            {
                apply();
                IsAvailable = true;
                ServiceMissing = false;
                _reason = null;
                _logged = false;
                Finish();
            }
            else
            {
                Fail(failure!, missing);
            }

            if (_dirty)
            {
                _dirty = false;
                Read();
            }
        });
    }

    private void Fail(string reason, bool missing = false)
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        IsAvailable = false;
        ServiceMissing = missing;
        _reason = reason;
        if (!_logged)
        {
            _logged = true;
            Log.Info($"{_indicator}: {reason}, the status reads as absent");
        }

        Finish();
    }

    private void Finish()
    {
        IsReady = true;
        Changed?.Invoke();
    }
}
