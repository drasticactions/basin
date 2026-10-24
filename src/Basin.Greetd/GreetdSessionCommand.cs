namespace Basin.Greetd;

/// <summary>
/// The command and the environment that a <see cref="GreetdStartSession"/> request carries.
/// </summary>
/// <param name="Cmd">The command elements. greetd joins them with spaces and runs the result through <c>/bin/sh -c</c>.</param>
/// <param name="Env">The <c>NAME=value</c> lines that greetd adds to the session environment.</param>
public sealed record GreetdSessionCommand(IReadOnlyList<string> Cmd, IReadOnlyList<string> Env);
