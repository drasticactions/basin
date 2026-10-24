namespace Basin.Notifications;

/// <summary>
/// The settings that <see cref="NotificationServer.TryCreate(ICompositorEventLoop, NotificationServerOptions, out string)"/> reads once, when it creates the server.
/// </summary>
public sealed record NotificationServerOptions
{
    /// <summary>
    /// The default value of <see cref="DefaultExpireMs"/>, in milliseconds.
    /// </summary>
    public const int DefaultExpireMilliseconds = 5000;

    /// <summary>
    /// Gets the D-Bus address to connect to, or <see langword="null"/> to use <c>DBUS_SESSION_BUS_ADDRESS</c>. There is no X11 fallback.
    /// </summary>
    public string? Address { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server takes the bus name from a current owner. This works only when that owner allows replacement.
    /// </summary>
    public bool Replace { get; init; }

    /// <summary>
    /// Gets a value indicating whether a later daemon can take the bus name from this server. <see cref="NotificationServer.NameLost"/> then occurs.
    /// </summary>
    public bool AllowReplacement { get; init; }

    /// <summary>
    /// Gets the timeout in milliseconds for a notification that requests -1. A value of zero or less means that such a notification never expires.
    /// </summary>
    public int DefaultExpireMs { get; init; } = DefaultExpireMilliseconds;

    /// <summary>
    /// Gets the server name that <c>GetServerInformation</c> returns.
    /// </summary>
    public string ServerName { get; init; } = "basin";

    /// <summary>
    /// Gets the vendor name that <c>GetServerInformation</c> returns.
    /// </summary>
    public string Vendor { get; init; } = "basin";

    /// <summary>
    /// Gets the server version that <c>GetServerInformation</c> returns. It is not the specification version.
    /// </summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Gets the longest time that <see cref="NotificationServer.TryCreate(ICompositorEventLoop, NotificationServerOptions, out string)"/> blocks to connect and request the name.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
