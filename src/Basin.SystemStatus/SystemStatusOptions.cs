namespace Basin.SystemStatus;

/// <summary>
/// The settings of a <see cref="SystemStatusBus"/>.
/// </summary>
public sealed class SystemStatusOptions
{
    /// <summary>
    /// Gets the D-Bus address to connect to. With <see langword="null"/>, the bus uses <c>DBUS_SYSTEM_BUS_ADDRESS</c>, else the system default. A test sets a private bus here.
    /// </summary>
    public string? Address { get; init; }

    /// <summary>
    /// Gets the time in milliseconds that a reader waits after a signal before it reads again. A burst of signals inside that time gives one read. A negative value counts as 0.
    /// </summary>
    public int CoalesceMilliseconds { get; init; } = 50;
}
