namespace Basin.Shell.River;

/// <summary>
/// The settings that a <see cref="RiverWindowManager"/> reads once, when it is constructed.
/// </summary>
public sealed record RiverWindowManagerOptions
{
    /// <summary>
    /// Gets the time in milliseconds that a transaction waits for windows to commit their new state.
    /// After this time the transaction applies whether or not every window has committed. It must be more than 0.
    /// </summary>
    public int TransactionTimeoutMs { get; init; } = Transaction.DefaultTimeoutMs;

    /// <summary>
    /// Gets the time in milliseconds that the window manager has to finish a manage or render sequence.
    /// After this time <see cref="RiverWindowManager.WindowManagerUnresponsive"/> fires. A value of 0 or less turns off the watchdog.
    /// </summary>
    public int UnresponsiveTimeoutMs { get; init; } = 2000;

    /// <summary>
    /// Gets a value indicating whether the compositor disconnects a window manager that misses <see cref="UnresponsiveTimeoutMs"/>.
    /// When it is <see langword="false"/>, the compositor only counts and reports the timeout.
    /// </summary>
    public bool DisconnectUnresponsiveManager { get; init; }

    /// <summary>
    /// Gets a value indicating whether new windows get a size while no window manager is bound.
    /// No code reads this value at this time.
    /// </summary>
    public bool ManageWithoutWindowManager { get; init; } = true;
}
