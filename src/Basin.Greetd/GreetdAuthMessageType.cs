using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// The kind of an <see cref="GreetdAuthMessage"/>, as the <c>auth_message_type</c> field gives it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GreetdAuthMessageType>))]
public enum GreetdAuthMessageType
{
    /// <summary>A question with an answer that the greeter can show, for example a user name or a one-time code.</summary>
    [JsonStringEnumMemberName("visible")]
    Visible,

    /// <summary>A question with an answer that the greeter must hide, for example a password.</summary>
    [JsonStringEnumMemberName("secret")]
    Secret,

    /// <summary>An information line. The greeter shows it and acknowledges it with no answer.</summary>
    [JsonStringEnumMemberName("info")]
    Info,

    /// <summary>An error line from PAM. The greeter shows it and acknowledges it with no answer.</summary>
    [JsonStringEnumMemberName("error")]
    Error,
}
