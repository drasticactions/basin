using Basin.Diagnostics;

namespace Basin.Greetd;

/// <summary>
/// The options for a <see cref="GreeterFlow"/>.
/// </summary>
public sealed record GreeterFlowOptions
{
    /// <summary>
    /// Gets the root prefix for <c>etc/passwd</c>, <c>etc/login.defs</c>, <c>var/lib/AccountsService/icons</c> and <c>usr/share</c>. The default is <c>/</c>.
    /// </summary>
    public string Root { get; init; } = "/";

    /// <summary>
    /// Gets the <c>NAME=value</c> lines that every <c>start_session</c> request carries, after the keyboard layout lines.
    /// </summary>
    public IReadOnlyList<string> Environment { get; init; } = [];

    /// <summary>
    /// Gets the error text that the flow shows after an authentication failure.
    /// </summary>
    public string LoginIncorrect { get; init; } = "Your username or password is incorrect. Try again.";

    /// <summary>
    /// Gets the text of the local secret prompt that the flow shows before it opens a conversation with greetd.
    /// </summary>
    public string PasswordPrompt { get; init; } = "Password: ";

    /// <summary>
    /// Gets the error text that the flow shows when authentication succeeds but no session is available.
    /// </summary>
    public string NoSessions { get; init; } = "No desktop sessions are installed. Ask your administrator to install one.";

    /// <summary>
    /// Gets a value indicating whether the flow asks systemd-homed for its users. A test turns it off to keep the D-Bus socket closed.
    /// </summary>
    public bool Homed { get; init; } = true;

    /// <summary>
    /// Gets the logger for the flow's warnings and debug lines.
    /// </summary>
    public BasinLogger Log { get; init; } = BasinLog.For("greeter");

    /// <summary>
    /// Reads a root prefix from an environment variable.
    /// </summary>
    /// <param name="variable">The name of the environment variable, for example <c>BASIN_GREETER_ROOT</c>.</param>
    /// <returns>The value of the variable, or <c>/</c> when it is not set or empty.</returns>
    public static string RootFromEnvironment(string variable) =>
        System.Environment.GetEnvironmentVariable(variable) is { Length: > 0 } prefix ? prefix : "/";
}
