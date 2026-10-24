using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// The <c>auth_message</c> response. greetd sends it when PAM asks a question or shows a line.
/// Each one needs a <see cref="GreetdPostAuthMessageResponse"/> reply.
/// </summary>
/// <param name="AuthMessageType">The kind of message, which selects the reply the greeter must send.</param>
/// <param name="Message">The text from PAM, for example a prompt or an info line.</param>
public sealed record GreetdAuthMessage(
    [property: JsonPropertyName("auth_message_type")] GreetdAuthMessageType AuthMessageType,
    [property: JsonPropertyName("auth_message")] string Message) : GreetdResponse;
