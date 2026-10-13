using System.Text.Json.Serialization;

namespace Basin.Greetd;

[JsonConverter(typeof(JsonStringEnumConverter<GreetdErrorType>))]
public enum GreetdErrorType
{
    [JsonStringEnumMemberName("auth_error")]
    AuthError,

    [JsonStringEnumMemberName("error")]
    Error,
}
