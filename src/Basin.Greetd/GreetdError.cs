using System.Text.Json.Serialization;

namespace Basin.Greetd;

public sealed record GreetdError(
    [property: JsonPropertyName("error_type")] GreetdErrorType ErrorType,
    [property: JsonPropertyName("description")] string Description) : GreetdResponse;
