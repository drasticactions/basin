using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// The <c>start_session</c> request. greetd accepts it after authentication and then waits for the greeter to exit.
/// </summary>
/// <param name="Cmd">The command elements. greetd joins them with spaces and runs the result through <c>/bin/sh -c</c>.</param>
/// <param name="Env">The <c>NAME=value</c> lines that greetd adds to the session environment.</param>
public sealed record GreetdStartSession(
    [property: JsonPropertyName("cmd")] IReadOnlyList<string> Cmd,
    [property: JsonPropertyName("env")] IReadOnlyList<string> Env) : GreetdRequest;
