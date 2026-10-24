using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// The kind of a <see cref="GreetdError"/>, as the <c>error_type</c> field gives it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GreetdErrorType>))]
public enum GreetdErrorType
{
    /// <summary>PAM refused the credentials. The greeter can ask again for the same user.</summary>
    [JsonStringEnumMemberName("auth_error")]
    AuthError,

    /// <summary>Any other failure, including a closed connection. It is not a wrong password.</summary>
    [JsonStringEnumMemberName("error")]
    Error,
}
