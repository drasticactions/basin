using System.Text.Json.Serialization;

namespace Basin.Greetd;

public sealed record GreetdPostAuthMessageResponse(
    [property: JsonPropertyName("response"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Response) : GreetdRequest;
