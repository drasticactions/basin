namespace Basin.Session;

/// <summary>
/// A power action that <see cref="LogindActions"/> can ask logind to do.
/// </summary>
public enum LogindAction
{
    /// <summary>
    /// Turns the machine off.
    /// </summary>
    PowerOff,

    /// <summary>
    /// Restarts the machine.
    /// </summary>
    Reboot,

    /// <summary>
    /// Suspends the machine to memory.
    /// </summary>
    Suspend,
}
