namespace Basin.Capabilities;

/// <summary>
/// What an <see cref="IKeyMonitor"/> decides about one key.
/// </summary>
public enum KeyMonitorVerdict
{
    /// <summary>
    /// The key continues to the bindings and the client.
    /// </summary>
    Pass,

    /// <summary>
    /// The key stops here.
    /// </summary>
    Block,
}
