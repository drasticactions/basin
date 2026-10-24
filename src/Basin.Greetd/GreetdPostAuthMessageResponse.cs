using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// The <c>post_auth_message_response</c> request. It replies to a <see cref="GreetdAuthMessage"/>.
/// </summary>
/// <param name="Response">The answer to a visible or secret prompt, or <see langword="null"/> to acknowledge an info or error line. A null value is not written.</param>
public sealed record GreetdPostAuthMessageResponse(
    [property: JsonPropertyName("response"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Response) : GreetdRequest;
