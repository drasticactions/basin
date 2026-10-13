using System.Text.Json.Serialization;

namespace Basin.Greetd;

public sealed record GreetdStartSession(
    [property: JsonPropertyName("cmd")] IReadOnlyList<string> Cmd,
    [property: JsonPropertyName("env")] IReadOnlyList<string> Env) : GreetdRequest;
