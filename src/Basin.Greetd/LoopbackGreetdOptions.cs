namespace Basin.Greetd;

/// <summary>
/// The conversation that a <see cref="LoopbackGreetd"/> serves.
/// </summary>
public sealed class LoopbackGreetdOptions
{
    /// <summary>
    /// Gets the info line that the server sends before the secret prompt. <see langword="null"/> goes straight to the secret prompt.
    /// </summary>
    public string? Info { get; init; } = "Demo mode";

    /// <summary>
    /// Gets the text of the secret prompt.
    /// </summary>
    public string Prompt { get; init; } = "Password:";

    /// <summary>
    /// Gets the password that the server accepts for any user.
    /// </summary>
    public string Password { get; init; } = "demo";
}
