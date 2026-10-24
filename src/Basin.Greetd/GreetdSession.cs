using Basin.Freedesktop;

namespace Basin.Greetd;

/// <summary>
/// A session that a greeter can offer, from a desktop entry, from the environments file or from a typed command.
/// </summary>
/// <param name="Id">The session id. For a desktop entry it is the desktop-file id without <c>.desktop</c>.</param>
/// <param name="Name">The name to show, localized for a desktop entry.</param>
/// <param name="Type">The kind of session, which sets how <see cref="Command"/> builds the request.</param>
/// <param name="Argv">The command arguments. For a <see cref="GreetdSessionType.Command"/> session it is one shell command line.</param>
/// <param name="Env">The <c>NAME=value</c> lines for the session, for example <c>XDG_SESSION_TYPE</c>.</param>
/// <param name="Wrapper">The arguments that go before the command, for example <c>startx</c> for an X11 session.</param>
/// <param name="Entry">The desktop entry that the session comes from, or <see langword="null"/> for a command.</param>
public sealed record GreetdSession(
    string Id,
    string Name,
    GreetdSessionType Type,
    IReadOnlyList<string> Argv,
    IReadOnlyList<string> Env,
    IReadOnlyList<string> Wrapper,
    DesktopEntry? Entry)
{
    /// <summary>
    /// Builds the <c>start_session</c> command. A desktop-entry session runs through <c>/usr/bin/env</c> with each element quoted for <c>sh</c>. A command session runs as it is.
    /// </summary>
    /// <param name="extraEnv">The <c>NAME=value</c> lines to add after <see cref="Env"/>, or <see langword="null"/> for none.</param>
    /// <returns>A new command and environment. Each call returns new lists.</returns>
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
