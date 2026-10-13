using System.Text.Json.Serialization;

namespace Basin.Greetd;

public sealed record GreetdAuthMessage(
    [property: JsonPropertyName("auth_message_type")] GreetdAuthMessageType AuthMessageType,
    [property: JsonPropertyName("auth_message")] string Message) : GreetdResponse;
