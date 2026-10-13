namespace Basin.Greetd;

public sealed class LoopbackGreetdOptions
{
    public string? Info { get; init; } = "Demo mode";

    public string Prompt { get; init; } = "Password:";

    public string Password { get; init; } = "demo";
}
