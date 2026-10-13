namespace Basin.Greetd;

public sealed record GreetdSessionCommand(IReadOnlyList<string> Cmd, IReadOnlyList<string> Env);
