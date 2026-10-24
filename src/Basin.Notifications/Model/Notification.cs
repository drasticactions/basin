namespace Basin.Notifications;

/// <summary>
/// One notification, immutable. A replacement is a new record with the same <see cref="Id"/>.
/// </summary>
public sealed record Notification
{
    /// <summary>
    /// Gets the id that the server assigned. It is never zero for a notification that the server holds. The server ignores the value that a caller sets.
    /// </summary>
    public uint Id { get; init; }

    /// <summary>
    /// Gets the application name that the client sent. It can be empty.
    /// </summary>
    public string AppName { get; init; } = "";

    /// <summary>
    /// Gets the icon name or <c>file://</c> URI that the client sent. It can be empty.
    /// </summary>
    public string AppIcon { get; init; } = "";

    /// <summary>
    /// Gets the single-line summary text.
    /// </summary>
    public string Summary { get; init; } = "";

    /// <summary>
    /// Gets the body as plain text, with markup removed. <see cref="BodySpans"/> holds the markup ranges.
    /// </summary>
    public string Body { get; init; } = "";

    /// <summary>
    /// Gets the ranges of <see cref="Body"/> that carried markup.
    /// </summary>
    public IReadOnlyList<NotificationSpan> BodySpans { get; init; } = [];

    /// <summary>
    /// Gets the actions in the order that the client sent them.
    /// </summary>
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];

    /// <summary>
    /// Gets the urgency from the <c>urgency</c> hint, or <see cref="NotificationUrgency.Normal"/> when the hint is missing or out of range.
    /// </summary>
    public NotificationUrgency Urgency { get; init; } = NotificationUrgency.Normal;

    /// <summary>
    /// Gets the <c>category</c> hint, for example <c>email.arrived</c>, or <see langword="null"/>.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets the <c>desktop-entry</c> hint, a desktop file id without <c>.desktop</c>, or <see langword="null"/>.
    /// </summary>
    public string? DesktopEntry { get; init; }

    /// <summary>
    /// Gets the raw image from <c>image-data</c>, else <c>image_data</c>, else <c>icon_data</c>, or <see langword="null"/>.
    /// </summary>
    public NotificationImage? Image { get; init; }

    /// <summary>
    /// Gets the <c>image-path</c> hint, else <c>image_path</c>, or <see langword="null"/> when both are missing or empty.
    /// </summary>
    public string? ImagePath { get; init; }

    /// <summary>
    /// Gets the <c>sound-file</c> hint, a path to a sound to play, or <see langword="null"/>.
    /// </summary>
    public string? SoundFile { get; init; }

    /// <summary>
    /// Gets the <c>sound-name</c> hint, a name from the sound naming specification, or <see langword="null"/>.
    /// </summary>
    public string? SoundName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client asked the shell to play no sound.
    /// </summary>
    public bool SuppressSound { get; init; }

    /// <summary>
    /// Gets a value indicating whether the notification stays after the user chooses an action.
    /// </summary>
    public bool Resident { get; init; }

    /// <summary>
    /// Gets a value indicating whether the shell must not keep the notification in a history after it closes.
    /// </summary>
    public bool Transient { get; init; }

    /// <summary>
    /// Gets a value indicating whether the action keys are icon names that the shell can show in place of the labels.
    /// </summary>
    public bool ActionIcons { get; init; }

    /// <summary>
    /// Gets the timeout that the client requested: -1 for the server default, 0 for never, otherwise milliseconds.
    /// </summary>
    public int ExpireTimeout { get; init; } = -1;

    /// <summary>
    /// Gets the timeout that the server applies, counted from the last add or replace. It is <see langword="null"/> when the notification never expires.
    /// </summary>
    public TimeSpan? ExpiresAfter { get; init; }

    /// <summary>
    /// Gets the unique bus name of the client that sent the notification. It is <see langword="null"/> for <see cref="NotificationServer.Post(Notification, uint)"/>.
    /// </summary>
    public string? Sender { get; init; }

    /// <summary>
    /// Tells whether one of <see cref="Actions"/> has a key, compared with case.
    /// </summary>
    /// <param name="key">The action key to look for.</param>
    /// <returns><see langword="true"/> when an action has that key.</returns>
    public bool HasAction(string key)
    {
        foreach (var action in Actions)
        {
            if (action.Key == key)
            {
                return true;
            }
        }

        return false;
    }
}
