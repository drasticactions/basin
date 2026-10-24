namespace Basin.Capabilities;

/// <summary>
/// A registry of global shortcuts that applications register.
/// The compositor policy decides which keys trigger each shortcut and calls <see cref="Trigger"/>.
/// </summary>
public interface IGlobalShortcuts
{
    /// <summary>
    /// Adds a shortcut and notifies the observers.
    /// </summary>
    /// <param name="shortcut">The shortcut to add.</param>
    /// <returns><see langword="false"/> when a shortcut with the same application and identifier is already registered.</returns>
    bool TryRegister(in GlobalShortcutInfo shortcut);

    /// <summary>
    /// Removes a shortcut and notifies the observers. An unknown shortcut is ignored.
    /// </summary>
    /// <param name="appId">The application that owns the shortcut.</param>
    /// <param name="id">The identifier of the shortcut.</param>
    void Unregister(string appId, string id);

    /// <summary>
    /// Copies all registered shortcuts into a span.
    /// </summary>
    /// <param name="shortcuts">The destination. It must be large enough for all shortcuts.</param>
    /// <returns>The number of shortcuts written, or -1 when <paramref name="shortcuts"/> is too small.</returns>
    int Enumerate(Span<GlobalShortcutInfo> shortcuts);

    /// <summary>
    /// Reports a press or a release of a shortcut to the observers.
    /// </summary>
    /// <param name="appId">The application that owns the shortcut.</param>
    /// <param name="id">The identifier of the shortcut.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <param name="timestampMs">The time of the event, in milliseconds on a monotonic clock.</param>
    /// <returns><see langword="false"/> when no such shortcut is registered.</returns>
    bool Trigger(string appId, string id, bool pressed, ulong timestampMs);

    /// <summary>Adds an observer that receives registry changes and activations.</summary>
    /// <param name="observer">The observer to add.</param>
    void AddObserver(IGlobalShortcutObserver observer);

    /// <summary>Removes an observer. An observer that was not added is ignored.</summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveObserver(IGlobalShortcutObserver observer);
}
