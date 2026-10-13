namespace Basin.Capabilities;

public interface IInputMethodClients
{
    bool HasInputMethodClient { get; }

    bool IsInputMethodClient(object? client);

    event Action? AvailabilityChanged;
}
