namespace Basin.Capabilities;

/// <summary>
/// Tells which Wayland client, if any, is the input method of the seat.
/// </summary>
public interface IInputMethodClients
{
    /// <summary>Gets a value indicating whether a client is bound as the input method now.</summary>
    bool HasInputMethodClient { get; }

    /// <summary>Tells whether a client is the bound input method.</summary>
    /// <param name="client">The client to examine. A value that is not a Wayland client gives <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="client"/> owns the live input method.</returns>
    bool IsInputMethodClient(object? client);

    /// <summary>Occurs after an input method client binds or goes away.</summary>
    event Action? AvailabilityChanged;
}
