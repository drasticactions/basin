namespace Basin.SoftKeyboard.Fcitx5;

/// <summary>
/// Holds the connection settings for <see cref="Fcitx5Keyboard.TryCreate(ICompositorEventLoop, Fcitx5KeyboardOptions, out string?)"/>.
/// </summary>
public sealed record Fcitx5KeyboardOptions
{
    /// <summary>
    /// Gets the D-Bus address to connect to. When it is <see langword="null"/> or empty, the value of DBUS_SESSION_BUS_ADDRESS is used.
    /// </summary>
    public string? Address { get; init; }

    /// <summary>
    /// Gets the time that the connection and the name request can take before creation fails. The default is 2 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
