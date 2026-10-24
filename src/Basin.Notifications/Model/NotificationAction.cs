namespace Basin.Notifications;

/// <summary>
/// One action that a client attaches to a notification.
/// </summary>
/// <param name="Key">The action key that <see cref="NotificationServer.Invoke(uint, string, string)"/> sends back to the client.</param>
/// <param name="Label">The text that the shell shows for the action.</param>
public readonly record struct NotificationAction(string Key, string Label)
{
    /// <summary>
    /// The key of the action that a click on the notification itself starts.
    /// </summary>
    public const string DefaultKey = "default";

    /// <summary>
    /// Gets a value indicating whether <see cref="Key"/> is <see cref="DefaultKey"/>.
    /// </summary>
    public bool IsDefault => Key == DefaultKey;
}
