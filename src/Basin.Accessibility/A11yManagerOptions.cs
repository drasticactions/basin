namespace Basin.Accessibility;

/// <summary>
/// The options of an <see cref="A11yManager"/>.
/// </summary>
public sealed record A11yManagerOptions
{
    /// <summary>
    /// Gets the address of the session bus, or <see langword="null"/> for <c>DBUS_SESSION_BUS_ADDRESS</c>.
    /// </summary>
    public string? Address { get; init; }

    /// <summary>
    /// Gets the longest time that <see cref="A11yManager.TryStart"/> waits for the bus. The default is 2 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets a value indicating whether the manager takes the bus name from another owner that allows it. The default is <see langword="false"/>.
    /// </summary>
    public bool Replace { get; init; }
}
