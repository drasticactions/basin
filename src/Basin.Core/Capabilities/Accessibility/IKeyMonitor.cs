namespace Basin.Capabilities;

/// <summary>
/// Sees each key before the compositor's bindings and before the focused client, and can hold it back.
/// An assistive technology uses it to watch keys and to take its own shortcuts.
/// </summary>
public interface IKeyMonitor
{
    /// <summary>
    /// Sees one key event. Call it on the compositor thread, before the key changes the xkb state.
    /// </summary>
    /// <param name="key">The key, with the xkb state from before the key.</param>
    /// <returns><see cref="KeyMonitorVerdict.Block"/> to hold the key back from the bindings, the xkb state and the client.</returns>
    KeyMonitorVerdict Filter(in KeyMonitorEvent key);
}
