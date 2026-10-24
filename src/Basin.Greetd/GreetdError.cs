using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// The <c>error</c> response. greetd sends it when a request fails, for example when authentication fails.
/// </summary>
/// <param name="ErrorType">Whether the failure is an authentication failure or another error.</param>
/// <param name="Description">The text that greetd gives for the failure.</param>
public sealed record GreetdError(
    [property: JsonPropertyName("error_type")] GreetdErrorType ErrorType,
    [property: JsonPropertyName("description")] string Description) : GreetdResponse;
