namespace Basin.SystemStatus;

/// <summary>
/// The result of <see cref="NetworkStatus.ActivateSecured"/>.
/// </summary>
/// <param name="Succeeded">A value indicating whether the connection became active.</param>
/// <param name="Error">Why the connection failed, in words for the user, or <see langword="null"/> on success.</param>
public sealed record NetworkActivation(bool Succeeded, string? Error)
{
    /// <summary>
    /// Gets the shared successful result.
    /// </summary>
    public static NetworkActivation Success { get; } = new(true, null);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">Why the connection failed.</param>
    /// <returns>A result with <see cref="Succeeded"/> set to false.</returns>
    public static NetworkActivation Failed(string error) => new(false, error);
}
