namespace Basin.Notifications;

/// <summary>
/// The <c>urgency</c> hint of a notification. The values are those of the specification.
/// </summary>
public enum NotificationUrgency : byte
{
    /// <summary>Low urgency.</summary>
    Low = 0,
    /// <summary>Normal urgency. This is the value when the client sends no valid hint.</summary>
    Normal = 1,
    /// <summary>Critical urgency. The server never expires it unless <see cref="NotificationServerOptions.CriticalExpires"/> is set.</summary>
    Critical = 2,
}
