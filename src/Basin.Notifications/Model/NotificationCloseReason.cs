namespace Basin.Notifications;

/// <summary>
/// The reason code that the <c>NotificationClosed</c> signal carries. The values are those of the specification.
/// </summary>
public enum NotificationCloseReason : uint
{
    /// <summary>The timeout ended, or the shell called <see cref="NotificationServer.Expire(uint)"/>.</summary>
    Expired = 1,
    /// <summary>The user dismissed the notification or chose one of its actions.</summary>
    Dismissed = 2,
    /// <summary>A client called <c>CloseNotification</c>.</summary>
    Closed = 3,
    /// <summary>A reason that the other values do not cover.</summary>
    Undefined = 4,
}
