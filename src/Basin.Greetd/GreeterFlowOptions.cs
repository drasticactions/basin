using Basin.Diagnostics;

namespace Basin.Greetd;

public sealed class GreeterFlowOptions
{
    public string Root { get; init; } = "/";

    public IReadOnlyList<string> Environment { get; init; } = [];

    public string LoginIncorrect { get; init; } = "Login incorrect";

    public string NoSessions { get; init; } = "No sessions are installed";

    public bool Homed { get; init; } = true;

    public BasinLogger Log { get; init; } = BasinLog.For("greeter");

    public static string RootFromEnvironment(string variable) =>
        System.Environment.GetEnvironmentVariable(variable) is { Length: > 0 } prefix ? prefix : "/";
}
