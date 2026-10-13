using Basin.Freedesktop;

namespace Basin.Greetd;

public sealed record GreetdSession(
    string Id,
    string Name,
    GreetdSessionType Type,
    IReadOnlyList<string> Argv,
    IReadOnlyList<string> Env,
    IReadOnlyList<string> Wrapper,
    DesktopEntry? Entry)
{
    public GreetdSessionCommand Command(IReadOnlyList<string>? extraEnv = null)
    {
        var env = new List<string>(Env);
        if (extraEnv is not null)
        {
            env.AddRange(extraEnv);
        }

        if (Type == GreetdSessionType.Command)
        {
            return new GreetdSessionCommand([.. Argv], env);
        }

        var cmd = new List<string>(Wrapper.Count + env.Count + Argv.Count + 1);
        foreach (var part in Wrapper)
        {
            cmd.Add(GreetdSessions.Quote(part));
        }

        cmd.Add("/usr/bin/env");
        foreach (var pair in env)
        {
            cmd.Add(GreetdSessions.Quote(pair));
        }

        foreach (var part in Argv)
        {
            cmd.Add(GreetdSessions.Quote(part));
        }

        return new GreetdSessionCommand(cmd, env);
    }
}
