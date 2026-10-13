using System.Text.Json.Serialization;

namespace Basin.Greetd;

public sealed record GreetdCreateSession([property: JsonPropertyName("username")] string Username) : GreetdRequest;
