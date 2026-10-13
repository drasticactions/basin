using System.Text.Json.Serialization;

namespace Basin.Greetd;

[JsonConverter(typeof(JsonStringEnumConverter<GreetdAuthMessageType>))]
public enum GreetdAuthMessageType
{
    [JsonStringEnumMemberName("visible")]
    Visible,

    [JsonStringEnumMemberName("secret")]
    Secret,

    [JsonStringEnumMemberName("info")]
    Info,

    [JsonStringEnumMemberName("error")]
    Error,
}
